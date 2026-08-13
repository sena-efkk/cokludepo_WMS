using Wms.Modules.Inventory.Domain;

namespace Wms.Api.Inventory;

public sealed record RelocateRequest(
    Guid? RequestId,
    Guid SkuId,
    Guid WarehouseId,
    Guid SourceLocationId,
    Guid DestinationLocationId,
    int Quantity);

public sealed record ChangeStatusRequest(
    Guid? RequestId,
    Guid SkuId,
    Guid WarehouseId,
    Guid LocationId,
    string FromStatus,
    string ToStatus,
    int Quantity);

public sealed record MovementResponse(
    Guid Id,
    Guid RequestId,
    string Type,
    Guid SkuId,
    Guid WarehouseId,
    Guid SourceLocationId,
    Guid? DestinationLocationId,
    string StatusFrom,
    string StatusTo,
    int Quantity,
    DateTime OccurredAt)
{
    public static MovementResponse From(InventoryMovement movement) =>
        new(
            movement.Id,
            movement.RequestId,
            movement.Type.ToString(),
            movement.SkuId,
            movement.WarehouseId,
            movement.SourceLocationId,
            movement.DestinationLocationId,
            movement.StatusFrom.ToString(),
            movement.StatusTo.ToString(),
            movement.Quantity,
            movement.OccurredAt);
}

public sealed record MovementResultResponse(string Outcome, Guid MovementId);

public sealed record ScannedRelocationRequest(
    Guid? RequestId,
    Guid WarehouseId,
    string? SourceLocationScan,
    string? SkuScan,
    string? DestinationLocationScan,
    int Quantity,
    string? DeviceId = null,
    string? OperatorId = null);

public sealed record ScannedRelocationResponse(
    string Status,
    string? RejectionCode,
    string? RejectionReason,
    Guid? MovementId,
    Guid? EvidenceId,
    Guid? SkuId,
    Guid? SourceLocationId,
    Guid? DestinationLocationId,
    int? Quantity)
{
    public static ScannedRelocationResponse From(Wms.Modules.Inventory.Application.Accuracy.Scanning.ScannedRelocationResult result) =>
        new(
            result.Status.ToString(),
            result.RejectionCode?.ToString(),
            result.RejectionReason,
            result.MovementId,
            result.EvidenceId,
            result.SkuId,
            result.SourceLocationId,
            result.DestinationLocationId,
            result.Quantity);
}
