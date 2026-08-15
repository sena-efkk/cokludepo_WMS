using Microsoft.Extensions.Options;
using Wms.Modules.Facility.Contracts;
using Wms.Modules.Inbound.Domain;
using Wms.Modules.Inventory.Contracts;
using Wms.Modules.MasterData.Contracts;

namespace Wms.Modules.Inbound.Application;

public enum ReceiveItemsOutcome
{
    Received = 1,
    AlreadyRecorded = 2,
}

public sealed record ReceiveItemsCommand(
    Guid RequestId,
    Guid ReceiptId,
    Guid ReceiptLineId,
    int Quantity,
    Guid ReceivingLocationId,
    ReceivingStockStatus ReceivingStatus);

public sealed record ReceiveItemsResult(
    ReceiveItemsOutcome Outcome,
    Guid ReceiveRecordId,
    ReceivingDisposition Disposition,
    int LineReceivedQuantity,
    Guid PutawayTaskId);

public sealed class ReceiveItems(
    IInboundStore store,
    IMasterDataQueryContract masterData,
    IFacilityQueryContract facility,
    IInventoryContract inventory,
    IOptions<InboundOptions> options)
{
    private static readonly HashSet<string> AllowedReceivingLocationTypes =
        new(StringComparer.OrdinalIgnoreCase) { "Receiving", "Staging" };

    public async Task<ReceiveItemsResult> Handle(ReceiveItemsCommand command, CancellationToken cancellationToken)
    {
        var existingRecord = await store.GetReceiveRecordByRequestIdAsync(command.RequestId, cancellationToken);
        if (existingRecord is not null)
        {
            return new ReceiveItemsResult(
                ReceiveItemsOutcome.AlreadyRecorded,
                existingRecord.Id,
                existingRecord.Disposition,
                0,
                Guid.Empty);
        }

        if (command.Quantity <= 0)
        {
            throw new ArgumentException("Receive quantity pozitif olmalıdır.", nameof(command.Quantity));
        }

        var receipt = await store.GetReceiptAsync(command.ReceiptId, cancellationToken)
            ?? throw new ReceiptNotFoundException(command.ReceiptId);

        if (receipt.Status is not (ReceiptStatus.Open or ReceiptStatus.PartiallyReceived))
        {
            throw new InvalidReceiptStateException($"Receipt {receipt.Status} durumundayken receive yapılamaz.");
        }

        var line = receipt.Lines.FirstOrDefault(l => l.Id == command.ReceiptLineId)
            ?? throw new ReceiptLineNotFoundException(command.ReceiptLineId);

        if (!options.Value.AllowOverReceipt && line.ReceivedQuantity + command.Quantity > line.ExpectedQuantity)
        {
            throw new OverReceiptNotAllowedException(line.ExpectedQuantity, line.ReceivedQuantity, command.Quantity);
        }

        var sku = await masterData.GetSkuAsync(line.SkuId, cancellationToken)
            ?? throw new InvalidReceiptStateException($"SKU bulunamadı: {line.SkuId}");
        if (!sku.IsActive)
        {
            throw new InvalidReceiptStateException($"SKU aktif değil: {sku.Code}");
        }

        var warehouse = await facility.GetWarehouseAsync(receipt.WarehouseId, cancellationToken)
            ?? throw new InvalidReceiptStateException($"Warehouse bulunamadı: {receipt.WarehouseId}");
        if (!warehouse.IsActive)
        {
            throw new InvalidReceiptStateException($"Warehouse aktif değil: {warehouse.Code}");
        }

        var receivingLocation = await facility.GetLocationAsync(command.ReceivingLocationId, cancellationToken)
            ?? throw new InvalidReceivingLocationException($"Receiving location bulunamadı: {command.ReceivingLocationId}");

        ValidateReceivingLocation(receipt.WarehouseId, receivingLocation);

        var inventoryStatus = command.ReceivingStatus.ToString().ToUpperInvariant();

        await store.BeginTransactionAsync(cancellationToken);
        try
        {
            await store.LockReceiptLineAsync(command.ReceiptLineId, cancellationToken);

            var freshReceipt = await store.GetReceiptAsync(command.ReceiptId, cancellationToken)
                ?? throw new ReceiptNotFoundException(command.ReceiptId);

            if (freshReceipt.Status is not (ReceiptStatus.Open or ReceiptStatus.PartiallyReceived))
            {
                throw new InvalidReceiptStateException($"Receipt {freshReceipt.Status} durumundayken receive yapılamaz.");
            }

            var freshLine = freshReceipt.Lines.FirstOrDefault(l => l.Id == command.ReceiptLineId)
                ?? throw new ReceiptLineNotFoundException(command.ReceiptLineId);

            if (!options.Value.AllowOverReceipt && freshLine.ReceivedQuantity + command.Quantity > freshLine.ExpectedQuantity)
            {
                throw new OverReceiptNotAllowedException(freshLine.ExpectedQuantity, freshLine.ReceivedQuantity, command.Quantity);
            }

            var duplicateRecord = await store.GetReceiveRecordByRequestIdAsync(command.RequestId, cancellationToken);
            if (duplicateRecord is not null)
            {
                await store.RollbackTransactionAsync(cancellationToken);
                return new ReceiveItemsResult(
                    ReceiveItemsOutcome.AlreadyRecorded,
                    duplicateRecord.Id,
                    duplicateRecord.Disposition,
                    freshLine.ReceivedQuantity,
                    Guid.Empty);
            }

            var inventoryResult = await inventory.ReceiveInventoryAsync(
                new ReceiveInventoryCommand(
                    command.RequestId,
                    freshLine.SkuId,
                    freshReceipt.WarehouseId,
                    command.ReceivingLocationId,
                    inventoryStatus,
                    command.Quantity,
                    "INBOUND_RECEIPT",
                    freshReceipt.Id),
                cancellationToken);

            if (inventoryResult.Outcome != ReceiveInventoryOutcome.Recorded
                && inventoryResult.Outcome != ReceiveInventoryOutcome.DuplicateRequest)
            {
                throw new InvalidOperationException($"Beklenmeyen inventory receive outcome: {inventoryResult.Outcome}");
            }

            var newTotal = freshLine.ReceivedQuantity + command.Quantity;
            var disposition = newTotal == freshLine.ExpectedQuantity
                ? ReceivingDisposition.Matched
                : newTotal > freshLine.ExpectedQuantity
                    ? ReceivingDisposition.Over
                    : ReceivingDisposition.Short;

            var record = ReceiptLineReceiveRecord.Create(
                command.RequestId,
                freshLine.Id,
                command.Quantity,
                disposition,
                command.ReceivingLocationId,
                inventoryStatus,
                command.RequestId);

            freshReceipt.RegisterReceive(freshLine.Id, command.Quantity, disposition, record.ReceivedAt);

            var task = PutawayTask.Create(
                freshReceipt.Id,
                freshLine.Id,
                record.Id,
                freshLine.SkuId,
                freshReceipt.WarehouseId,
                command.ReceivingLocationId,
                inventoryStatus,
                command.Quantity);

            await store.AddReceiveRecordAsync(record, cancellationToken);
            await store.AddPutawayTaskAsync(task, cancellationToken);

            var saveOutcome = await store.SaveChangesAsync(cancellationToken);
            if (saveOutcome == InboundSaveOutcome.DuplicateRequest)
            {
                await store.RollbackTransactionAsync(cancellationToken);
                var winner = await store.GetReceiveRecordByRequestIdAsync(command.RequestId, cancellationToken);
                if (winner is not null)
                {
                    return new ReceiveItemsResult(
                        ReceiveItemsOutcome.AlreadyRecorded,
                        winner.Id,
                        winner.Disposition,
                        0,
                        Guid.Empty);
                }

                throw new InvalidOperationException($"Receive record çakıştı ama bulunamadı: {command.RequestId}");
            }

            await store.CommitTransactionAsync(cancellationToken);

            return new ReceiveItemsResult(
                ReceiveItemsOutcome.Received,
                record.Id,
                disposition,
                freshLine.ReceivedQuantity,
                task.Id);
        }
        catch
        {
            await store.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }

    private static void ValidateReceivingLocation(Guid warehouseId, LocationInfo location)
    {
        if (!location.IsActive)
        {
            throw new InvalidReceivingLocationException($"Receiving location aktif değil: {location.Code}");
        }

        if (location.WarehouseId != warehouseId)
        {
            throw new InvalidReceivingLocationException($"Receiving location {location.Code} verilen warehouse'a ait değil.");
        }

        if (!location.HoldsInventory)
        {
            throw new InvalidReceivingLocationException($"Receiving location {location.Code} stok tutamaz (HoldsInventory=false).");
        }

        if (!AllowedReceivingLocationTypes.Contains(location.LocationType))
        {
            throw new InvalidReceivingLocationException(
                $"Location {location.Code} ({location.LocationType}) receiving kabul etmez — RECEIVING veya STAGING tipi gerekir.");
        }
    }
}
