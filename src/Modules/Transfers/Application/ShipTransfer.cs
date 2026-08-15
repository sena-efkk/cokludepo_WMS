using Wms.Modules.Inbound.Contracts;
using Wms.Modules.Outbound.Contracts;
using Wms.Modules.Transfers.Domain;

namespace Wms.Modules.Transfers.Application;

public enum ShipTransferOutcome
{
    Shipped = 1,
    AlreadyShipped = 2,
}

public sealed record ShipTransferCommand(
    Guid TransferId,
    string? TrackingNumber = null,
    string? CarrierCode = null);

public sealed record ShipTransferResult(
    ShipTransferOutcome Outcome,
    Guid TransferId,
    Guid? ShipmentId,
    string? ShipmentNumber,
    Guid? InboundReceiptId);

public sealed class ShipTransfer(
    ITransferStore store,
    IOutboundContract outbound,
    IInboundContract inbound)
{
    public async Task<ShipTransferResult> Handle(ShipTransferCommand command, CancellationToken cancellationToken)
    {
        var transfer = await store.GetTransferAsync(command.TransferId, cancellationToken)
            ?? throw new TransferNotFoundException(command.TransferId);

        if (transfer.Status == TransferStatus.InTransit || transfer.Status == TransferStatus.Receiving)
        {
            return new ShipTransferResult(
                ShipTransferOutcome.AlreadyShipped,
                transfer.Id,
                null,
                null,
                transfer.InboundReceiptId);
        }

        if (transfer.Status != TransferStatus.Allocated)
        {
            throw new InvalidTransferStateException($"Transfer {transfer.Status} durumundayken ship edilemez.");
        }

        if (transfer.OutboundOrderId is null)
        {
            throw new InvalidTransferStateException("Allocate edilmemiş transfer ship edilemez.");
        }

        var shipRequestId = CreateTransfer.DeriveChildRequestId(transfer.Id, "SHIP");

        // 1) Source shipment — Inventory consume (idempotent; retry AlreadyShipped).
        var shipResult = await outbound.ShipOrderAsync(
            shipRequestId,
            transfer.OutboundOrderId.Value,
            command.TrackingNumber,
            command.CarrierCode,
            cancellationToken);

        // 2) Destination receipt (idempotent; aynı transfer hep aynı receipt'ı üretir).
        var receiptRequestId = CreateTransfer.DeriveChildRequestId(transfer.Id, "DEST-RECEIPT");
        var receipt = await inbound.CreateReceiptAsync(
            receiptRequestId,
            $"TRF-IN-{transfer.TransferNumber}",
            transfer.DestinationWarehouseId,
            transfer.TransferNumber,
            "TRANSFER",
            transfer.Lines
                .Select(l => new InboundReceiptLineInput(l.SkuId, l.RequestedQuantity))
                .ToList(),
            cancellationToken);

        // 3) Transfer state + line correlation (tek transaction; crash sonrası retry güvenli).
        await store.BeginTransactionAsync(cancellationToken);
        try
        {
            var fresh = await store.GetTransferAsync(command.TransferId, cancellationToken)
                ?? throw new TransferNotFoundException(command.TransferId);

            if (fresh.Status == TransferStatus.InTransit || fresh.Status == TransferStatus.Receiving)
            {
                await store.CommitTransactionAsync(cancellationToken);
                return new ShipTransferResult(ShipTransferOutcome.AlreadyShipped, transfer.Id, null, null, fresh.InboundReceiptId);
            }

            if (fresh.Status != TransferStatus.Allocated)
            {
                throw new InvalidTransferStateException($"Transfer {fresh.Status} durumundayken ship edilemez.");
            }

            var receiptInfo = await inbound.GetReceiptAsync(receipt.ReceiptId, cancellationToken);
            foreach (var line in fresh.Lines)
            {
                line.MarkShipped(line.RequestedQuantity);
                var receiptLine = receiptInfo!.Lines.First(l => l.SkuId == line.SkuId);
                line.SetInboundReceiptLine(receiptLine.Id);
            }

            fresh.MarkShipped(receipt.ReceiptId);

            await store.SaveChangesAsync(cancellationToken);
            await store.CommitTransactionAsync(cancellationToken);

            return new ShipTransferResult(
                shipResult.Outcome == OutboundShipOutcome.Shipped ? ShipTransferOutcome.Shipped : ShipTransferOutcome.AlreadyShipped,
                transfer.Id,
                shipResult.ShipmentId,
                shipResult.ShipmentNumber,
                receipt.ReceiptId);
        }
        catch
        {
            await store.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }
}
