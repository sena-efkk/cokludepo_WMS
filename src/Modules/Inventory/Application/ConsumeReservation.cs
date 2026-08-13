using Wms.Modules.Inventory.Domain;

namespace Wms.Modules.Inventory.Application;

public sealed class ConsumeReservation(IInventoryStore store)
{
    public async Task Handle(Guid reservationId, CancellationToken cancellationToken)
    {
        var reservation = await store.GetReservationAsync(reservationId, cancellationToken)
            ?? throw new ReservationNotFoundException(reservationId);

        if (reservation.Status == ReservationStatus.Consumed)
        {
            return;
        }

        if (reservation.Status != ReservationStatus.Allocated)
        {
            throw new InvalidReservationStateException(
                $"Yalnızca ALLOCATED rezervasyon consume edilebilir. Mevcut: {reservation.Status}.");
        }

        var ledgerEntries = new List<InventoryLedgerEntry>();
        foreach (var line in reservation.Lines)
        {
            var balance = await store.GetBalanceAsync(
                    reservation.WarehouseId,
                    reservation.SkuId,
                    line.LocationId,
                    InventoryStatus.Available,
                    cancellationToken)
                ?? throw new InvalidOperationException(
                    $"Rezervasyon satırı için AVAILABLE balance bulunamadı: location={line.LocationId}");

            balance.Consume(line.Quantity);
            ledgerEntries.Add(InventoryLedgerEntry.Create(
                reservation.RequestId,
                reservation.SkuId,
                reservation.WarehouseId,
                line.LocationId,
                InventoryStatus.Available,
                LedgerEntryType.ReservationConsumed,
                -line.Quantity,
                -line.Quantity));
        }

        reservation.MarkConsumed();
        await store.AddLedgerEntriesAsync(ledgerEntries, cancellationToken);
        _ = await store.SaveChangesAsync(cancellationToken);
    }
}
