using Wms.Modules.Transfers.Domain;

namespace Wms.Modules.Transfers.Application;

public sealed record TransferLineQuery(
    Guid Id,
    Guid SkuId,
    int RequestedQuantity,
    int ShippedQuantity,
    int ReceivedQuantity,
    int ConfirmedVarianceQuantity,
    int InTransitQuantity,
    bool IsClosed,
    Guid? OutboundOrderLineId,
    Guid? InboundReceiptLineId);

public sealed record TransferDiscrepancyQuery(
    Guid Id,
    Guid RequestId,
    Guid TransferLineId,
    int Quantity,
    TransferDiscrepancyReason Reason,
    string? Note,
    DateTime CreatedAt);

public sealed record TransferQuery(
    Guid Id,
    Guid RequestId,
    string TransferNumber,
    Guid SourceWarehouseId,
    Guid DestinationWarehouseId,
    string? ExternalReference,
    TransferStatus Status,
    Guid? OutboundOrderId,
    Guid? InboundReceiptId,
    int InTransitQuantity,
    DateTime CreatedAt,
    DateTime? ShippedAt,
    DateTime? CompletedAt,
    DateTime? CancelledAt,
    IReadOnlyList<TransferLineQuery> Lines,
    IReadOnlyList<TransferDiscrepancyQuery> Discrepancies);

public sealed record TransferSummary(
    Guid Id,
    string TransferNumber,
    Guid SourceWarehouseId,
    Guid DestinationWarehouseId,
    TransferStatus Status,
    int InTransitQuantity,
    DateTime CreatedAt);

public sealed class GetTransfer(ITransferStore store)
{
    public async Task<TransferQuery?> Handle(Guid transferId, CancellationToken cancellationToken)
    {
        var transfer = await store.GetTransferAsync(transferId, cancellationToken);
        if (transfer is null)
        {
            return null;
        }

        var discrepancies = new List<TransferDiscrepancyQuery>();
        foreach (var line in transfer.Lines)
        {
            var lineDiscrepancies = await store.ListDiscrepanciesAsync(line.Id, cancellationToken);
            discrepancies.AddRange(lineDiscrepancies.Select(d => new TransferDiscrepancyQuery(
                d.Id,
                d.RequestId,
                d.TransferLineId,
                d.Quantity,
                d.Reason,
                d.Note,
                d.CreatedAt)));
        }

        return new TransferQuery(
            transfer.Id,
            transfer.RequestId,
            transfer.TransferNumber,
            transfer.SourceWarehouseId,
            transfer.DestinationWarehouseId,
            transfer.ExternalReference,
            transfer.Status,
            transfer.OutboundOrderId,
            transfer.InboundReceiptId,
            transfer.InTransitQuantity,
            transfer.CreatedAt,
            transfer.ShippedAt,
            transfer.CompletedAt,
            transfer.CancelledAt,
            transfer.Lines
                .Select(l => new TransferLineQuery(
                    l.Id,
                    l.SkuId,
                    l.RequestedQuantity,
                    l.ShippedQuantity,
                    l.ReceivedQuantity,
                    l.ConfirmedVarianceQuantity,
                    l.InTransitQuantity,
                    l.IsClosed,
                    l.OutboundOrderLineId,
                    l.InboundReceiptLineId))
                .ToList(),
            discrepancies);
    }
}

public sealed class ListTransfers(ITransferStore store)
{
    public async Task<IReadOnlyList<TransferSummary>> Handle(Guid? warehouseId, int limit, CancellationToken cancellationToken)
    {
        var transfers = await store.ListTransfersAsync(warehouseId, limit, cancellationToken);
        return transfers
            .Select(t => new TransferSummary(
                t.Id,
                t.TransferNumber,
                t.SourceWarehouseId,
                t.DestinationWarehouseId,
                t.Status,
                t.InTransitQuantity,
                t.CreatedAt))
            .ToList();
    }
}
