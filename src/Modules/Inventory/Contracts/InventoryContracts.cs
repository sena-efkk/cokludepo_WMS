using Wms.Modules.Inventory.Domain;

namespace Wms.Modules.Inventory.Contracts;

public sealed record AvailabilityInfo(int OnHand, int Allocated, int Available);

public sealed record ReservationCreatedInfo(Guid ReservationId, Guid RequestId, int Quantity, IReadOnlyList<ReservationLineInfo> Lines);

public sealed record ReservationLineInfo(Guid LocationId, int Quantity);

public interface IInventoryContract
{
    Task<AvailabilityInfo> GetAvailabilityAsync(Guid warehouseId, Guid skuId, CancellationToken cancellationToken);

    Task<ReservationCreatedInfo> ReserveAsync(
        Guid requestId,
        Guid skuId,
        Guid warehouseId,
        int quantity,
        string purpose,
        CancellationToken cancellationToken);

    Task ReleaseReservationAsync(Guid reservationId, CancellationToken cancellationToken);

    Task ConsumeReservationAsync(Guid reservationId, CancellationToken cancellationToken);

    Task ReportPickNotFoundAsync(
        Guid requestId,
        Guid skuId,
        Guid warehouseId,
        Guid locationId,
        string? sourceReferenceId,
        CancellationToken cancellationToken);
}
