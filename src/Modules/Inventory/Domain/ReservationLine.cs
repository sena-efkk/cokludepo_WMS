namespace Wms.Modules.Inventory.Domain;

public sealed class ReservationLine
{
    private ReservationLine()
    {
    }

    private ReservationLine(Guid reservationId, Guid locationId, int quantity)
    {
        Id = Guid.NewGuid();
        ReservationId = reservationId;
        LocationId = locationId;
        Quantity = quantity;
    }

    public Guid Id { get; private set; }

    public Guid ReservationId { get; private set; }

    public Guid LocationId { get; private set; }

    public int Quantity { get; private set; }

    public static ReservationLine Create(Guid reservationId, Guid locationId, int quantity)
    {
        return new ReservationLine(reservationId, locationId, quantity);
    }
}
