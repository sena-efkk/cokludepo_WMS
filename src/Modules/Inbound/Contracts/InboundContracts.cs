namespace Wms.Modules.Inbound.Contracts;

public sealed record InboundReceiptLineInput(Guid SkuId, int ExpectedQuantity);

public sealed record InboundReceiptCreated(Guid ReceiptId, string ReceiptNumber);

public enum InboundReceiveOutcome
{
    Received = 1,
    AlreadyRecorded = 2,
}

public sealed record InboundReceiveResult(
    InboundReceiveOutcome Outcome,
    Guid ReceiveRecordId,
    string Disposition,
    int LineReceivedQuantity);

public sealed record InboundReceiptLineInfo(Guid Id, Guid SkuId, int ExpectedQuantity, int ReceivedQuantity);

public sealed record InboundReceiptInfo(
    Guid Id,
    string ReceiptNumber,
    Guid WarehouseId,
    string Status,
    IReadOnlyList<InboundReceiptLineInfo> Lines);

public interface IInboundContract
{
    Task<InboundReceiptCreated> CreateReceiptAsync(
        Guid requestId,
        string? receiptNumber,
        Guid warehouseId,
        string? externalReference,
        string? sourceType,
        IReadOnlyList<InboundReceiptLineInput> lines,
        CancellationToken cancellationToken);

    Task<InboundReceiptInfo?> GetReceiptAsync(Guid receiptId, CancellationToken cancellationToken);

    Task<InboundReceiveResult> ReceiveAsync(
        Guid requestId,
        Guid receiptId,
        Guid receiptLineId,
        int quantity,
        Guid receivingLocationId,
        string receivingStatus,
        CancellationToken cancellationToken);
}
