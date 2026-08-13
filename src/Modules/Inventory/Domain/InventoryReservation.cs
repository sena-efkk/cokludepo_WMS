namespace Wms.Modules.Inventory.Domain;

public sealed class InventoryReservation : IHasTimestamps
{
    private readonly List<ReservationLine> _lines = [];

    private InventoryReservation()
    {
    }

    private InventoryReservation(Guid requestId, Guid skuId, Guid warehouseId, int requestedQuantity)
    {
        Id = Guid.NewGuid();
        RequestId = requestId;
        SkuId = skuId;
        WarehouseId = warehouseId;
        RequestedQuantity = requestedQuantity;
        Status = ReservationStatus.Allocated;
        var now = DateTime.UtcNow;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid RequestId { get; private set; }

    public Guid SkuId { get; private set; }

    public Guid WarehouseId { get; private set; }

    public int RequestedQuantity { get; private set; }

    public ReservationStatus Status { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime UpdatedAt { get; set; }

    public IReadOnlyCollection<ReservationLine> Lines => _lines;

    public static InventoryReservation Create(Guid requestId, Guid skuId, Guid warehouseId, int requestedQuantity)
    {
        if (requestId == Guid.Empty)
        {
            throw new ArgumentException("RequestId boş olamaz.", nameof(requestId));
        }

        if (skuId == Guid.Empty)
        {
            throw new ArgumentException("Reservation bir SKU'ya bağlı olmalıdır.", nameof(skuId));
        }

        if (warehouseId == Guid.Empty)
        {
            throw new ArgumentException("Reservation bir Warehouse'a bağlı olmalıdır.", nameof(warehouseId));
        }

        if (requestedQuantity <= 0)
        {
            throw new ArgumentException("İstenen miktar pozitif olmalıdır.", nameof(requestedQuantity));
        }

        return new InventoryReservation(requestId, skuId, warehouseId, requestedQuantity);
    }

    public void AddLine(Guid locationId, int quantity)
    {
        if (locationId == Guid.Empty)
        {
            throw new ArgumentException("Line bir Location'a bağlı olmalıdır.", nameof(locationId));
        }

        if (quantity <= 0)
        {
            throw new ArgumentException("Line miktarı pozitif olmalıdır.", nameof(quantity));
        }

        if (_lines.Sum(l => l.Quantity) + quantity > RequestedQuantity)
        {
            throw new InvalidOperationException("Line toplamları istenen miktarı aşamaz.");
        }

        _lines.Add(ReservationLine.Create(Id, locationId, quantity));
    }

    public void MarkReleased()
    {
        EnsureAllocated("release");
        Status = ReservationStatus.Released;
    }

    public void MarkConsumed()
    {
        EnsureAllocated("consume");
        Status = ReservationStatus.Consumed;
    }

    private void EnsureAllocated(string operation)
    {
        if (Status != ReservationStatus.Allocated)
        {
            throw new InvalidOperationException($"Yalnızca ALLOCATED rezervasyon {operation} edilebilir. Mevcut: {Status}.");
        }
    }
}
