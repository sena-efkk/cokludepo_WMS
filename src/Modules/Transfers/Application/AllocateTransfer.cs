using Wms.Modules.Outbound.Contracts;
using Wms.Modules.Transfers.Domain;

namespace Wms.Modules.Transfers.Application;

public enum AllocateTransferOutcome
{
    Allocated = 1,
    AlreadyAllocated = 2,
    InsufficientStock = 3,
}

public sealed record AllocateTransferResult(AllocateTransferOutcome Outcome, Guid TransferId, Guid? OutboundOrderId);

public sealed class AllocateTransfer(
    ITransferStore store,
    IOutboundContract outbound)
{
    public async Task<AllocateTransferResult> Handle(Guid transferId, CancellationToken cancellationToken)
    {
        var transfer = await store.GetTransferAsync(transferId, cancellationToken)
            ?? throw new TransferNotFoundException(transferId);

        if (transfer.Status == TransferStatus.Allocated && transfer.OutboundOrderId is not null)
        {
            return new AllocateTransferResult(AllocateTransferOutcome.AlreadyAllocated, transfer.Id, transfer.OutboundOrderId);
        }

        if (transfer.Status != TransferStatus.Created)
        {
            throw new InvalidTransferStateException($"Transfer {transfer.Status} durumundayken allocate edilemez.");
        }

        // Stable correlation: aynı transfer hep aynı outbound order'ı üretir (idempotent).
        var sourceOrderRequestId = CreateTransfer.DeriveChildRequestId(transfer.Id, "SOURCE-ORDER");

        var created = await outbound.CreateOrderAsync(
            sourceOrderRequestId,
            $"TRF-SO-{transfer.TransferNumber}",
            transfer.SourceWarehouseId,
            transfer.TransferNumber,
            transfer.Lines
                .Select(l => new OutboundOrderLineInput(l.SkuId, l.RequestedQuantity))
                .ToList(),
            cancellationToken);

        var allocateResult = await outbound.AllocateOrderAsync(created.OrderId, cancellationToken);
        if (allocateResult.Outcome == OutboundAllocateOutcome.InsufficientStock)
        {
            return new AllocateTransferResult(AllocateTransferOutcome.InsufficientStock, transfer.Id, created.OrderId);
        }

        await store.BeginTransactionAsync(cancellationToken);
        try
        {
            var fresh = await store.GetTransferAsync(transferId, cancellationToken)
                ?? throw new TransferNotFoundException(transferId);

            if (fresh.Status == TransferStatus.Allocated)
            {
                await store.CommitTransactionAsync(cancellationToken);
                return new AllocateTransferResult(AllocateTransferOutcome.AlreadyAllocated, transfer.Id, fresh.OutboundOrderId);
            }

            fresh.MarkAllocated(created.OrderId);
            await store.SaveChangesAsync(cancellationToken);
            await store.CommitTransactionAsync(cancellationToken);

            return new AllocateTransferResult(AllocateTransferOutcome.Allocated, transfer.Id, created.OrderId);
        }
        catch
        {
            await store.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }
}
