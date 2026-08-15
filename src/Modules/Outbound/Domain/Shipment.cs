namespace Wms.Modules.Outbound.Domain;

public sealed class Shipment : IHasTimestamps
{
    private Shipment()
    {
        ShipmentNumber = string.Empty;
    }

    private Shipment(
        Guid orderId,
        Guid requestId,
        string shipmentNumber,
        string? trackingNumber,
        string? carrierCode)
    {
        Id = Guid.NewGuid();
        OrderId = orderId;
        RequestId = requestId;
        ShipmentNumber = shipmentNumber;
        TrackingNumber = trackingNumber;
        CarrierCode = carrierCode;
        Status = ShipmentStatus.Created;
        var now = DateTime.UtcNow;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid OrderId { get; private set; }

    public Guid RequestId { get; private set; }

    public string ShipmentNumber { get; private set; }

    public ShipmentStatus Status { get; private set; }

    public string? TrackingNumber { get; private set; }

    public string? CarrierCode { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime? ShippedAt { get; private set; }

    public DateTime UpdatedAt { get; set; }

    public static Shipment Create(
        Guid orderId,
        Guid requestId,
        string shipmentNumber,
        string? trackingNumber,
        string? carrierCode)
    {
        if (orderId == Guid.Empty)
        {
            throw new ArgumentException("Shipment bir order'a bağlı olmalıdır.", nameof(orderId));
        }

        if (requestId == Guid.Empty)
        {
            throw new ArgumentException("Shipment bir RequestId taşımalıdır.", nameof(requestId));
        }

        if (string.IsNullOrWhiteSpace(shipmentNumber))
        {
            throw new ArgumentException("Shipment number boş olamaz.", nameof(shipmentNumber));
        }

        return new Shipment(
            orderId,
            requestId,
            shipmentNumber.Trim().ToUpperInvariant(),
            string.IsNullOrWhiteSpace(trackingNumber) ? null : trackingNumber.Trim(),
            string.IsNullOrWhiteSpace(carrierCode) ? null : carrierCode.Trim().ToUpperInvariant());
    }

    public void MarkShipped(DateTime? at = null)
    {
        if (Status == ShipmentStatus.Shipped)
        {
            return;
        }

        if (Status != ShipmentStatus.Created)
        {
            throw new InvalidOperationException($"Yalnızca CREATED shipment ship edilebilir. Mevcut: {Status}");
        }

        Status = ShipmentStatus.Shipped;
        ShippedAt = at ?? DateTime.UtcNow;
    }
}
