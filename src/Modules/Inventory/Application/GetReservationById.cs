using Wms.Modules.Inventory.Domain;

namespace Wms.Modules.Inventory.Application;

public sealed class GetReservationById(IInventoryStore store)
{
    public async Task<InventoryReservation?> Handle(Guid reservationId, CancellationToken cancellationToken) =>
        await store.GetReservationAsync(reservationId, cancellationToken);
}
