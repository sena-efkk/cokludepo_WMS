namespace Wms.Modules.Outbound.Domain;

public sealed class FulfillmentOrderLine : IHasTimestamps
{
    private FulfillmentOrderLine()
    {
    }

    private FulfillmentOrderLine(Guid orderId, Guid skuId, int requestedQuantity)
    {
        Id = Guid.NewGuid();
        OrderId = orderId;
        SkuId = skuId;
        RequestedQuantity = requestedQuantity;
        ReservationId = null;
        var now = DateTime.UtcNow;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid OrderId { get; private set; }

    public Guid SkuId { get; private set; }

    public int RequestedQuantity { get; private set; }

    public Guid? ReservationId { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime UpdatedAt { get; set; }

    public static FulfillmentOrderLine Create(Guid orderId, Guid skuId, int requestedQuantity)
    {
        if (orderId == Guid.Empty)
        {
            throw new ArgumentException("Order line bir order'a bağlı olmalıdır.", nameof(orderId));
        }

        if (skuId == Guid.Empty)
        {
            throw new ArgumentException("Order line bir SKU'ya bağlı olmalıdır.", nameof(skuId));
        }

        if (requestedQuantity <= 0)
        {
            throw new ArgumentException("Requested quantity pozitif olmalıdır.", nameof(requestedQuantity));
        }

        return new FulfillmentOrderLine(orderId, skuId, requestedQuantity);
    }

    public void SetReservation(Guid reservationId)
    {
        if (reservationId == Guid.Empty)
        {
            throw new ArgumentException("Reservation id boş olamaz.", nameof(reservationId));
        }

        ReservationId = reservationId;
    }
}
