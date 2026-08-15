using Wms.Modules.Transfers.Application;
using Wms.Modules.Transfers.Domain;

namespace Wms.Api.Transfers;

public sealed record CreateTransferLineRequest(Guid SkuId, int RequestedQuantity);

public sealed record CreateTransferRequest(
    Guid? RequestId,
    string? TransferNumber,
    Guid SourceWarehouseId,
    Guid DestinationWarehouseId,
    string? ExternalReference,
    IReadOnlyList<CreateTransferLineRequest> Lines);

public sealed record CreateTransferResponse(string Outcome, Guid TransferId, string TransferNumber);

public sealed record AllocateTransferResponse(string Outcome, Guid TransferId, Guid? OutboundOrderId);

public sealed record ShipTransferRequest(string? TrackingNumber, string? CarrierCode);

public sealed record ShipTransferResponse(string Outcome, Guid TransferId, Guid? ShipmentId, string? ShipmentNumber, Guid? InboundReceiptId);

public sealed record ReceiveTransferRequest(Guid? RequestId, Guid TransferLineId, int Quantity, Guid ReceivingLocationId, string ReceivingStatus);

public sealed record ReceiveTransferResponse(
    string Outcome,
    Guid TransferId,
    Guid TransferLineId,
    int LineReceivedQuantity,
    int LineInTransitQuantity);

public sealed record ConfirmVarianceRequest(Guid? RequestId, Guid TransferLineId, int Quantity, string Reason, string? Note);

public sealed record ConfirmVarianceResponse(
    string Outcome,
    Guid TransferId,
    Guid TransferLineId,
    Guid? DiscrepancyId,
    int LineInTransitQuantity,
    bool TransferCompleted);

public sealed record TransferLineResponse(
    Guid Id,
    Guid SkuId,
    int RequestedQuantity,
    int ShippedQuantity,
    int ReceivedQuantity,
    int ConfirmedVarianceQuantity,
    int InTransitQuantity,
    bool IsClosed,
    Guid? OutboundOrderLineId,
    Guid? InboundReceiptLineId)
{
    public static TransferLineResponse From(TransferLineQuery line) =>
        new(
            line.Id,
            line.SkuId,
            line.RequestedQuantity,
            line.ShippedQuantity,
            line.ReceivedQuantity,
            line.ConfirmedVarianceQuantity,
            line.InTransitQuantity,
            line.IsClosed,
            line.OutboundOrderLineId,
            line.InboundReceiptLineId);
}

public sealed record TransferDiscrepancyResponse(
    Guid Id,
    Guid RequestId,
    Guid TransferLineId,
    int Quantity,
    string Reason,
    string? Note,
    DateTime CreatedAt)
{
    public static TransferDiscrepancyResponse From(TransferDiscrepancyQuery d) =>
        new(d.Id, d.RequestId, d.TransferLineId, d.Quantity, d.Reason.ToString(), d.Note, d.CreatedAt);
}

public sealed record TransferResponse(
    Guid Id,
    Guid RequestId,
    string TransferNumber,
    Guid SourceWarehouseId,
    Guid DestinationWarehouseId,
    string? ExternalReference,
    string Status,
    Guid? OutboundOrderId,
    Guid? InboundReceiptId,
    int InTransitQuantity,
    DateTime CreatedAt,
    DateTime? ShippedAt,
    DateTime? CompletedAt,
    DateTime? CancelledAt,
    IReadOnlyList<TransferLineResponse> Lines,
    IReadOnlyList<TransferDiscrepancyResponse> Discrepancies)
{
    public static TransferResponse From(TransferQuery transfer) =>
        new(
            transfer.Id,
            transfer.RequestId,
            transfer.TransferNumber,
            transfer.SourceWarehouseId,
            transfer.DestinationWarehouseId,
            transfer.ExternalReference,
            transfer.Status.ToString(),
            transfer.OutboundOrderId,
            transfer.InboundReceiptId,
            transfer.InTransitQuantity,
            transfer.CreatedAt,
            transfer.ShippedAt,
            transfer.CompletedAt,
            transfer.CancelledAt,
            transfer.Lines.Select(TransferLineResponse.From).ToList(),
            transfer.Discrepancies.Select(TransferDiscrepancyResponse.From).ToList());
}

public sealed record TransferSummaryResponse(
    Guid Id,
    string TransferNumber,
    Guid SourceWarehouseId,
    Guid DestinationWarehouseId,
    string Status,
    int InTransitQuantity,
    DateTime CreatedAt)
{
    public static TransferSummaryResponse From(TransferSummary summary) =>
        new(
            summary.Id,
            summary.TransferNumber,
            summary.SourceWarehouseId,
            summary.DestinationWarehouseId,
            summary.Status.ToString(),
            summary.InTransitQuantity,
            summary.CreatedAt);
}
