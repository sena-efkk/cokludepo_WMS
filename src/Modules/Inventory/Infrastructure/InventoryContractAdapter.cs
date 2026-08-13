using Wms.Modules.Inventory.Application;
using Wms.Modules.Inventory.Application.Accuracy;
using Wms.Modules.Inventory.Contracts;
using Wms.Modules.Inventory.Domain.Accuracy;

namespace Wms.Modules.Inventory.Infrastructure;

public sealed class InventoryContractAdapter(
    Reserve reserve,
    ReleaseReservation releaseReservation,
    ConsumeReservation consumeReservation,
    GetWarehouseSkuSummary summary,
    ReportPickNotFound reportPickNotFound) : IInventoryContract
{
    public async Task<AvailabilityInfo> GetAvailabilityAsync(Guid warehouseId, Guid skuId, CancellationToken cancellationToken)
    {
        var result = await summary.Handle(warehouseId, skuId, cancellationToken);
        return new AvailabilityInfo(result.OnHand, result.Allocated, result.Available);
    }

    public async Task<ReservationCreatedInfo> ReserveAsync(
        Guid requestId,
        Guid skuId,
        Guid warehouseId,
        int quantity,
        string purpose,
        CancellationToken cancellationToken)
    {
        var reservation = await reserve.Handle(
            new ReserveCommand(requestId, skuId, warehouseId, quantity, purpose),
            cancellationToken);

        return new ReservationCreatedInfo(
            reservation.Id,
            reservation.RequestId,
            reservation.RequestedQuantity,
            reservation.Lines.Select(l => new ReservationLineInfo(l.LocationId, l.Quantity)).ToList());
    }

    public Task ReleaseReservationAsync(Guid reservationId, CancellationToken cancellationToken)
    {
        return releaseReservation.Handle(reservationId, cancellationToken);
    }

    public Task ConsumeReservationAsync(Guid reservationId, CancellationToken cancellationToken)
    {
        return consumeReservation.Handle(reservationId, cancellationToken);
    }

    public async Task ReportPickNotFoundAsync(
        Guid requestId,
        Guid skuId,
        Guid warehouseId,
        Guid locationId,
        string? sourceReferenceId,
        CancellationToken cancellationToken)
    {
        await reportPickNotFound.Handle(
            new ReportPickNotFoundCommand(
                requestId,
                skuId,
                warehouseId,
                locationId,
                AccuracySourceType.Pick,
                string.IsNullOrWhiteSpace(sourceReferenceId) ? null : Guid.Parse(sourceReferenceId),
                null),
            cancellationToken);
    }
}
