namespace Wms.Modules.Outbound.Domain;

public sealed class PickTask : IHasTimestamps
{
    private PickTask()
    {
    }

    private PickTask(
        Guid orderId,
        Guid orderLineId,
        Guid reservationId,
        Guid reservationLineId,
        Guid warehouseId,
        Guid locationId,
        Guid skuId,
        int requiredQuantity)
    {
        Id = Guid.NewGuid();
        OrderId = orderId;
        OrderLineId = orderLineId;
        ReservationId = reservationId;
        ReservationLineId = reservationLineId;
        WarehouseId = warehouseId;
        LocationId = locationId;
        SkuId = skuId;
        RequiredQuantity = requiredQuantity;
        PickedQuantity = 0;
        Status = PickTaskStatus.Pending;
        var now = DateTime.UtcNow;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid OrderId { get; private set; }

    public Guid OrderLineId { get; private set; }

    public Guid ReservationId { get; private set; }

    public Guid ReservationLineId { get; private set; }

    public Guid WarehouseId { get; private set; }

    public Guid LocationId { get; private set; }

    public Guid SkuId { get; private set; }

    public int RequiredQuantity { get; private set; }

    public int PickedQuantity { get; private set; }

    public PickTaskStatus Status { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime? StartedAt { get; private set; }

    public DateTime? CompletedAt { get; private set; }

    public DateTime UpdatedAt { get; set; }

    public static PickTask Create(
        Guid orderId,
        Guid orderLineId,
        Guid reservationId,
        Guid reservationLineId,
        Guid warehouseId,
        Guid locationId,
        Guid skuId,
        int requiredQuantity)
    {
        if (orderId == Guid.Empty || orderLineId == Guid.Empty)
        {
            throw new ArgumentException("Pick task bir order/line'a bağlı olmalıdır.");
        }

        if (reservationId == Guid.Empty || reservationLineId == Guid.Empty)
        {
            throw new ArgumentException("Pick task bir reservation line'dan üretilmelidir.");
        }

        if (warehouseId == Guid.Empty || locationId == Guid.Empty || skuId == Guid.Empty)
        {
            throw new ArgumentException("Pick task; warehouse, location ve SKU zorunludur.");
        }

        if (requiredQuantity <= 0)
        {
            throw new ArgumentException("Pick task required quantity pozitif olmalıdır.", nameof(requiredQuantity));
        }

        return new PickTask(
            orderId,
            orderLineId,
            reservationId,
            reservationLineId,
            warehouseId,
            locationId,
            skuId,
            requiredQuantity);
    }

    public void Start(DateTime? at = null)
    {
        if (Status == PickTaskStatus.Completed)
        {
            throw new InvalidOperationException("Tamamlanmış pick task başlatılamaz.");
        }

        if (Status == PickTaskStatus.NotFound)
        {
            throw new InvalidOperationException("NOT_FOUND pick task başlatılamaz.");
        }

        if (Status == PickTaskStatus.Cancelled)
        {
            throw new InvalidOperationException("İptal edilmiş pick task başlatılamaz.");
        }

        if (Status == PickTaskStatus.Pending)
        {
            Status = PickTaskStatus.InProgress;
            StartedAt = at ?? DateTime.UtcNow;
        }
    }

    public void ConfirmPicked(int quantity, DateTime? at = null)
    {
        if (Status is PickTaskStatus.Completed or PickTaskStatus.NotFound or PickTaskStatus.Cancelled)
        {
            throw new InvalidOperationException($"Pick task {Status} durumundayken confirm edilemez.");
        }

        if (quantity <= 0)
        {
            throw new ArgumentException("Confirm quantity pozitif olmalıdır.", nameof(quantity));
        }

        if (PickedQuantity + quantity > RequiredQuantity)
        {
            throw new InvalidOperationException(
                $"Confirm quantity reservation'ı aşıyor: required {RequiredQuantity}, picked {PickedQuantity}, attempt {quantity}.");
        }

        if (Status == PickTaskStatus.Pending)
        {
            Status = PickTaskStatus.InProgress;
            StartedAt = at ?? DateTime.UtcNow;
        }

        PickedQuantity += quantity;
        if (PickedQuantity == RequiredQuantity)
        {
            Status = PickTaskStatus.Completed;
            CompletedAt = at ?? DateTime.UtcNow;
        }
    }

    public void MarkNotFound(DateTime? at = null)
    {
        if (Status is PickTaskStatus.Completed or PickTaskStatus.NotFound or PickTaskStatus.Cancelled)
        {
            throw new InvalidOperationException($"Pick task {Status} durumundayken NotFound işaretlenemez.");
        }

        Status = PickTaskStatus.NotFound;
        CompletedAt = at ?? DateTime.UtcNow;
    }

    public void Cancel()
    {
        if (Status is PickTaskStatus.Pending or PickTaskStatus.InProgress)
        {
            Status = PickTaskStatus.Cancelled;
        }
    }
}
