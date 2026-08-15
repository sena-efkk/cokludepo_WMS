using Wms.Modules.Facility.Contracts;
using Wms.Modules.Inbound.Domain;
using Wms.Modules.Inventory.Contracts;
using Wms.Modules.MasterData.Contracts;
using Wms.Integration.Contracts;
using Wms.Integration.Outbox;

namespace Wms.Modules.Inbound.Application;

public enum PutawayCompletionStatus
{
    Completed = 1,
    Rejected = 2,
    AlreadyCompleted = 3,
}

public sealed record CompletePutawayCommand(
    Guid TaskId,
    Guid RequestId,
    string SourceScan,
    string SkuScan,
    string DestinationScan,
    int Quantity,
    string? DeviceId = null,
    string? OperatorId = null);

public sealed record CompletePutawayResult(
    PutawayCompletionStatus Status,
    Guid TaskId,
    Guid? MovementId,
    string? RejectionCode,
    string? RejectionReason);

public sealed class CompletePutaway(
    IInboundStore store,
    IMasterDataQueryContract masterData,
    IFacilityQueryContract facility,
    IInventoryContract inventory)
{
    public async Task<CompletePutawayResult> Handle(CompletePutawayCommand command, CancellationToken cancellationToken)
    {
        var task = await store.GetPutawayTaskAsync(command.TaskId, cancellationToken)
            ?? throw new PutawayTaskNotFoundException(command.TaskId);

        if (task.Status == PutawayTaskStatus.Completed)
        {
            return new CompletePutawayResult(PutawayCompletionStatus.AlreadyCompleted, task.Id, task.MovementId, null, null);
        }

        if (task.Status == PutawayTaskStatus.Cancelled)
        {
            throw new InvalidPutawayTaskStateException("Putaway task iptal edilmiş.");
        }

        if (command.Quantity != task.Quantity)
        {
            throw new PutawayQuantityMismatchException(task.Quantity, command.Quantity);
        }

        if (task.InventoryStatus != "AVAILABLE")
        {
            return new CompletePutawayResult(
                PutawayCompletionStatus.Rejected,
                task.Id,
                null,
                "PUTAWAY_STATUS_NOT_SUPPORTED",
                $"Non-AVAILABLE putaway bu fazda desteklenmiyor: {task.InventoryStatus}. Status-change hareketi ayrı bir işlemdir.");
        }

        var sourceLocation = await facility.GetLocationByCodeAsync(task.WarehouseId, command.SourceScan.Trim(), cancellationToken);
        if (sourceLocation is null || sourceLocation.Id != task.SourceLocationId)
        {
            throw new PutawaySourceMismatchException(
                task.SourceLocationId.ToString(),
                sourceLocation?.Code ?? command.SourceScan.Trim());
        }

        var sku = await masterData.GetSkuByBarcodeAsync(command.SkuScan.Trim(), cancellationToken);
        if (sku is null || sku.Id != task.SkuId)
        {
            throw new PutawaySkuMismatchException(task.SkuId, command.SkuScan.Trim());
        }

        var relocation = await inventory.ExecuteScannedRelocationAsync(
            new ScannedRelocationContractCommand(
                command.RequestId,
                task.WarehouseId,
                command.SourceScan.Trim(),
                command.SkuScan.Trim(),
                command.DestinationScan.Trim(),
                command.Quantity,
                command.DeviceId,
                command.OperatorId),
            cancellationToken);

        if (relocation.Status == ScannedRelocationContractStatus.Rejected)
        {
            return new CompletePutawayResult(
                PutawayCompletionStatus.Rejected,
                task.Id,
                null,
                relocation.RejectionCode,
                relocation.RejectionReason);
        }

        var movementId = relocation.MovementId
            ?? throw new InvalidOperationException($"Scanned relocation hareket üretmedi: {command.RequestId}");

        await store.BeginTransactionAsync(cancellationToken);
        try
        {
            var freshTask = await store.GetPutawayTaskAsync(command.TaskId, cancellationToken)
                ?? throw new PutawayTaskNotFoundException(command.TaskId);

            freshTask.Complete(movementId);
            await store.SaveChangesAsync(cancellationToken);

            var receipt = await store.GetReceiptAsync(freshTask.ReceiptId, cancellationToken)
                ?? throw new ReceiptNotFoundException(freshTask.ReceiptId);

            var wasCompleted = receipt.Status == ReceiptStatus.Completed;

            var counts = await store.GetPutawayTaskCountsAsync(freshTask.ReceiptId, cancellationToken);
            receipt.OnPutawayTaskCompleted(counts.Total == counts.Completed);

            // Integration event: receipt completion + outbox AYNI transaction'da (atomic).
            if (!wasCompleted && receipt.Status == ReceiptStatus.Completed)
            {
                var receiptEvent = new ReceiptCompletedV1(
                    receipt.Id,
                    receipt.CompletedAt ?? DateTime.UtcNow,
                    receipt.Id,
                    receipt.ReceiptNumber,
                    receipt.WarehouseId,
                    receipt.Id);
                var outbox = OutboxMessage.Create(
                    receipt.Id,
                    IntegrationEventTypes.ReceiptCompleted,
                    IntegrationEventTypes.CurrentVersion,
                    receiptEvent,
                    receipt.CompletedAt ?? DateTime.UtcNow,
                    correlationId: receipt.Id);
                await store.AddOutboxMessageAsync(outbox, cancellationToken);
            }

            await store.SaveChangesAsync(cancellationToken);
            await store.CommitTransactionAsync(cancellationToken);
        }
        catch
        {
            await store.RollbackTransactionAsync(cancellationToken);
            throw;
        }

        return new CompletePutawayResult(PutawayCompletionStatus.Completed, task.Id, movementId, null, null);
    }
}
