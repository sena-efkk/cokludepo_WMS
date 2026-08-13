using Wms.Modules.Inventory.Application;
using Wms.Modules.Inventory.Domain;

namespace Wms.Api.Inventory;

public sealed record RecordOpeningBalanceRequest(
    Guid? RequestId,
    Guid SkuId,
    Guid WarehouseId,
    Guid LocationId,
    string Status,
    int Quantity);

public sealed record OpeningBalanceResponse(string Outcome, Guid RequestId);

public sealed record BalanceResponse(
    Guid SkuId,
    Guid WarehouseId,
    Guid LocationId,
    string Status,
    int Quantity,
    int Allocated,
    int Available)
{
    public static BalanceResponse From(BalanceView view) =>
        new(view.SkuId, view.WarehouseId, view.LocationId, view.Status.ToString(), view.Quantity, view.Allocated, view.Available);
}

public sealed record StatusQuantityResponse(string Status, int Quantity);

public sealed record WarehouseSkuResponse(
    Guid SkuId,
    Guid WarehouseId,
    int OnHand,
    int Allocated,
    int Available,
    IReadOnlyList<StatusQuantityResponse> ByStatus)
{
    public static WarehouseSkuResponse From(WarehouseSkuSummary summary) =>
        new(
            summary.SkuId,
            summary.WarehouseId,
            summary.OnHand,
            summary.Allocated,
            summary.Available,
            summary.ByStatus.Select(s => new StatusQuantityResponse(s.Status.ToString(), s.Quantity)).ToList());
}

public sealed record ReserveRequest(Guid? RequestId, Guid SkuId, Guid WarehouseId, int Quantity, string? Purpose);

public sealed record ReservationLineResponse(Guid LocationId, int Quantity);

public sealed record ReservationResponse(
    Guid Id,
    Guid RequestId,
    Guid SkuId,
    Guid WarehouseId,
    int RequestedQuantity,
    string Status,
    IReadOnlyList<ReservationLineResponse> Lines)
{
    public static ReservationResponse From(InventoryReservation reservation) =>
        new(
            reservation.Id,
            reservation.RequestId,
            reservation.SkuId,
            reservation.WarehouseId,
            reservation.RequestedQuantity,
            reservation.Status.ToString(),
            reservation.Lines.Select(l => new ReservationLineResponse(l.LocationId, l.Quantity)).ToList());
}

public sealed record LedgerEntryResponse(
    Guid Id,
    Guid RequestId,
    Guid SkuId,
    Guid WarehouseId,
    Guid LocationId,
    string Status,
    string EntryType,
    int QuantityDelta,
    int AllocatedDelta,
    DateTime OccurredAt)
{
    public static LedgerEntryResponse From(InventoryLedgerEntry entry) =>
        new(
            entry.Id,
            entry.RequestId,
            entry.SkuId,
            entry.WarehouseId,
            entry.LocationId,
            entry.Status.ToString(),
            entry.EntryType.ToString(),
            entry.QuantityDelta,
            entry.AllocatedDelta,
            entry.OccurredAt);
}
