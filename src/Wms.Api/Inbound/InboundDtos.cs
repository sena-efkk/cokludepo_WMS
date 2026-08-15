using Wms.Modules.Inbound.Application;
using Wms.Modules.Inbound.Domain;

namespace Wms.Api.Inbound;

public sealed record CreateReceiptLineRequest(Guid SkuId, int ExpectedQuantity);

public sealed record CreateReceiptRequest(
    Guid? RequestId,
    string? ReceiptNumber,
    Guid WarehouseId,
    string? ExternalReference,
    string? SourceType,
    IReadOnlyList<CreateReceiptLineRequest> Lines);

public sealed record CreateReceiptResponse(string Outcome, Guid ReceiptId, string ReceiptNumber);

public sealed record ReceiveItemsRequest(
    Guid? RequestId,
    Guid ReceiptLineId,
    int Quantity,
    Guid ReceivingLocationId,
    string ReceivingStatus);

public sealed record ReceiveItemsResponse(
    string Outcome,
    Guid ReceiveRecordId,
    string Disposition,
    int LineReceivedQuantity,
    Guid PutawayTaskId);

public sealed record ReceiptLineResponse(
    Guid Id,
    Guid SkuId,
    int ExpectedQuantity,
    int ReceivedQuantity,
    string? Disposition);

public sealed record ReceiveRecordResponse(
    Guid Id,
    Guid RequestId,
    Guid ReceiptLineId,
    int Quantity,
    string Disposition,
    Guid ReceivingLocationId,
    string InventoryStatus,
    Guid InventoryOperationId,
    DateTime ReceivedAt);

public sealed record ReceiptResponse(
    Guid Id,
    Guid RequestId,
    string ReceiptNumber,
    Guid WarehouseId,
    string? ExternalReference,
    string? SourceType,
    string Status,
    DateTime CreatedAt,
    DateTime? ReceivingStartedAt,
    DateTime? CompletedAt,
    DateTime? CancelledAt,
    IReadOnlyList<ReceiptLineResponse> Lines,
    IReadOnlyList<ReceiveRecordResponse> ReceiveRecords)
{
    public static ReceiptResponse From(ReceiptQuery receipt) =>
        new(
            receipt.Id,
            receipt.RequestId,
            receipt.ReceiptNumber,
            receipt.WarehouseId,
            receipt.ExternalReference,
            receipt.SourceType,
            receipt.Status.ToString(),
            receipt.CreatedAt,
            receipt.ReceivingStartedAt,
            receipt.CompletedAt,
            receipt.CancelledAt,
            receipt.Lines
                .Select(l => new ReceiptLineResponse(l.Id, l.SkuId, l.ExpectedQuantity, l.ReceivedQuantity, l.Disposition?.ToString()))
                .ToList(),
            receipt.ReceiveRecords
                .Select(r => new ReceiveRecordResponse(
                    r.Id,
                    r.RequestId,
                    r.ReceiptLineId,
                    r.Quantity,
                    r.Disposition.ToString(),
                    r.ReceivingLocationId,
                    r.InventoryStatus,
                    r.InventoryOperationId,
                    r.ReceivedAt))
                .ToList());
}

public sealed record ReceiptSummaryResponse(
    Guid Id,
    string ReceiptNumber,
    Guid WarehouseId,
    string? ExternalReference,
    string Status,
    DateTime CreatedAt,
    int TotalExpected,
    int TotalReceived)
{
    public static ReceiptSummaryResponse From(ReceiptSummary summary) =>
        new(
            summary.Id,
            summary.ReceiptNumber,
            summary.WarehouseId,
            summary.ExternalReference,
            summary.Status.ToString(),
            summary.CreatedAt,
            summary.TotalExpected,
            summary.TotalReceived);
}

public sealed record PutawayTaskResponse(
    Guid Id,
    Guid ReceiptId,
    Guid ReceiptLineId,
    Guid ReceiveRecordId,
    Guid SkuId,
    Guid WarehouseId,
    Guid SourceLocationId,
    string InventoryStatus,
    int Quantity,
    string Status,
    Guid? MovementId,
    DateTime CreatedAt,
    DateTime? StartedAt,
    DateTime? CompletedAt)
{
    public static PutawayTaskResponse From(PutawayTaskQuery task) =>
        new(
            task.Id,
            task.ReceiptId,
            task.ReceiptLineId,
            task.ReceiveRecordId,
            task.SkuId,
            task.WarehouseId,
            task.SourceLocationId,
            task.InventoryStatus,
            task.Quantity,
            task.Status.ToString(),
            task.MovementId,
            task.CreatedAt,
            task.StartedAt,
            task.CompletedAt);
}

public sealed record CompletePutawayRequest(
    Guid? RequestId,
    string SourceScan,
    string SkuScan,
    string DestinationScan,
    int Quantity,
    string? DeviceId = null,
    string? OperatorId = null);

public sealed record CompletePutawayResponse(
    string Status,
    Guid TaskId,
    Guid? MovementId,
    string? RejectionCode,
    string? RejectionReason);
