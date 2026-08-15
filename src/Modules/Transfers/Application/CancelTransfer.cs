using Wms.Modules.Outbound.Contracts;
using Wms.Modules.Transfers.Domain;

namespace Wms.Modules.Transfers.Application;

public sealed class CancelTransfer(
    ITransferStore store,
    IOutboundContract outbound)
{
    public async Task<TransferOrder> Handle(Guid transferId, CancellationToken cancellationToken)
    {
        var transfer = await store.GetTransferAsync(transferId, cancellationToken)
            ?? throw new TransferNotFoundException(transferId);

        if (transfer.Status == TransferStatus.Cancelled)
        {
            return transfer;
        }

        if (transfer.Status is not (TransferStatus.Created or TransferStatus.Allocated))
        {
            throw new InvalidTransferStateException(
                $"Shipment sonrası transfer iptal edilemez ({transfer.Status}) — ürün InTransit'tedir; reversal explicit workflow gerektirir.");
        }

        // Reservation release: outbound order cancel (Inventory ReleaseReservation üzerinden).
        if (transfer.OutboundOrderId is not null)
        {
            await outbound.CancelOrderAsync(transfer.OutboundOrderId.Value, cancellationToken);
        }

        await store.BeginTransactionAsync(cancellationToken);
        try
        {
            var fresh = await store.GetTransferAsync(transferId, cancellationToken)
                ?? throw new TransferNotFoundException(transferId);
            fresh.Cancel();
            await store.SaveChangesAsync(cancellationToken);
            await store.CommitTransactionAsync(cancellationToken);
            return fresh;
        }
        catch
        {
            await store.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }
}
