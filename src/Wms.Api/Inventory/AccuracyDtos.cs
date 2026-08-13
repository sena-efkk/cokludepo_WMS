using Wms.Modules.Inventory.Domain.Accuracy;

namespace Wms.Api.Inventory;

public sealed record ReportPickNotFoundRequest(
    Guid? RequestId,
    Guid SkuId,
    Guid WarehouseId,
    Guid LocationId,
    string? SourceReferenceId,
    DateTime? OccurredAt);

public sealed record ReportSignalResultResponse(string Outcome, Guid SignalId);

public sealed record AccuracySignalResponse(
    Guid Id,
    Guid RequestId,
    string SignalType,
    string SourceType,
    Guid SkuId,
    Guid WarehouseId,
    Guid LocationId,
    Guid? SourceReferenceId,
    DateTime OccurredAt,
    DateTime RecordedAt,
    int SystemQuantityAtSignal,
    int AllocatedAtSignal,
    int AvailableAtSignal,
    string StatusAtSignal)
{
    public static AccuracySignalResponse From(InventoryAccuracySignal signal) =>
        new(
            signal.Id,
            signal.RequestId,
            signal.SignalType.ToString(),
            signal.SourceType.ToString(),
            signal.SkuId,
            signal.WarehouseId,
            signal.LocationId,
            signal.SourceReferenceId,
            signal.OccurredAt,
            signal.RecordedAt,
            signal.SystemQuantityAtSignal,
            signal.AllocatedAtSignal,
            signal.AvailableAtSignal,
            signal.StatusAtSignal.ToString());
}
