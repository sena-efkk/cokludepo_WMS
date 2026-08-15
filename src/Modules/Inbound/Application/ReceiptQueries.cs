using Wms.Modules.Inbound.Domain;

namespace Wms.Modules.Inbound.Application;

public sealed record ReceiptQueryLine(
    Guid Id,
    Guid SkuId,
    int ExpectedQuantity,
    int ReceivedQuantity,
    ReceivingDisposition? Disposition);

public sealed record ReceiptReceiveRecordQuery(
    Guid Id,
    Guid RequestId,
    Guid ReceiptLineId,
    int Quantity,
    ReceivingDisposition Disposition,
    Guid ReceivingLocationId,
    string InventoryStatus,
    Guid InventoryOperationId,
    DateTime ReceivedAt);

public sealed record ReceiptQuery(
    Guid Id,
    Guid RequestId,
    string ReceiptNumber,
    Guid WarehouseId,
    string? ExternalReference,
    string? SourceType,
    ReceiptStatus Status,
    DateTime CreatedAt,
    DateTime? ReceivingStartedAt,
    DateTime? CompletedAt,
    DateTime? CancelledAt,
    IReadOnlyList<ReceiptQueryLine> Lines,
    IReadOnlyList<ReceiptReceiveRecordQuery> ReceiveRecords);

public sealed record ReceiptSummary(
    Guid Id,
    string ReceiptNumber,
    Guid WarehouseId,
    string? ExternalReference,
    ReceiptStatus Status,
    DateTime CreatedAt,
    int TotalExpected,
    int TotalReceived);

public sealed class GetReceipt(IInboundStore store)
{
    public async Task<ReceiptQuery?> Handle(Guid receiptId, CancellationToken cancellationToken)
    {
        var receipt = await store.GetReceiptAsync(receiptId, cancellationToken);
        if (receipt is null)
        {
            return null;
        }

        var records = new List<ReceiptReceiveRecordQuery>();
        foreach (var line in receipt.Lines)
        {
            var lineRecords = await store.ListReceiveRecordsAsync(line.Id, cancellationToken);
            records.AddRange(lineRecords.Select(r => new ReceiptReceiveRecordQuery(
                r.Id,
                r.RequestId,
                r.ReceiptLineId,
                r.Quantity,
                r.Disposition,
                r.ReceivingLocationId,
                r.InventoryStatus,
                r.InventoryOperationId,
                r.ReceivedAt)));
        }

        return new ReceiptQuery(
            receipt.Id,
            receipt.RequestId,
            receipt.ReceiptNumber,
            receipt.WarehouseId,
            receipt.ExternalReference,
            receipt.SourceType,
            receipt.Status,
            receipt.CreatedAt,
            receipt.ReceivingStartedAt,
            receipt.CompletedAt,
            receipt.CancelledAt,
            receipt.Lines
                .Select(l => new ReceiptQueryLine(l.Id, l.SkuId, l.ExpectedQuantity, l.ReceivedQuantity, l.Disposition))
                .ToList(),
            records);
    }
}

public sealed class ListReceipts(IInboundStore store)
{
    public async Task<IReadOnlyList<ReceiptSummary>> Handle(Guid? warehouseId, int limit, CancellationToken cancellationToken)
    {
        var receipts = await store.ListReceiptsAsync(warehouseId, limit, cancellationToken);
        return receipts
            .Select(r => new ReceiptSummary(
                r.Id,
                r.ReceiptNumber,
                r.WarehouseId,
                r.ExternalReference,
                r.Status,
                r.CreatedAt,
                r.Lines.Sum(l => l.ExpectedQuantity),
                r.Lines.Sum(l => l.ReceivedQuantity)))
            .ToList();
    }
}
