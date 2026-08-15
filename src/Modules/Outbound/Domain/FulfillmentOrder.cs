namespace Wms.Modules.Outbound.Domain;

public sealed record OrderLineSpec(Guid SkuId, int RequestedQuantity);

public sealed class FulfillmentOrder : IHasTimestamps
{
    private readonly List<FulfillmentOrderLine> _lines = [];

    private FulfillmentOrder()
    {
        OrderNumber = string.Empty;
    }

    private FulfillmentOrder(
        Guid requestId,
        string orderNumber,
        Guid warehouseId,
        string? externalOrderReference)
    {
        Id = Guid.NewGuid();
        RequestId = requestId;
        OrderNumber = orderNumber;
        WarehouseId = warehouseId;
        ExternalOrderReference = externalOrderReference;
        Status = OrderStatus.Created;
        var now = DateTime.UtcNow;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid RequestId { get; private set; }

    public string OrderNumber { get; private set; }

    public Guid WarehouseId { get; private set; }

    public string? ExternalOrderReference { get; private set; }

    public OrderStatus Status { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime? AllocatedAt { get; private set; }

    public DateTime? PickingStartedAt { get; private set; }

    public DateTime? PackedAt { get; private set; }

    public DateTime? ShippedAt { get; private set; }

    public DateTime? CancelledAt { get; private set; }

    public DateTime UpdatedAt { get; set; }

    public IReadOnlyCollection<FulfillmentOrderLine> Lines => _lines;

    public static FulfillmentOrder Create(
        Guid requestId,
        string orderNumber,
        Guid warehouseId,
        string? externalOrderReference,
        IReadOnlyList<OrderLineSpec> lineSpecs)
    {
        if (requestId == Guid.Empty)
        {
            throw new ArgumentException("Order bir RequestId taşımalıdır.", nameof(requestId));
        }

        if (string.IsNullOrWhiteSpace(orderNumber))
        {
            throw new ArgumentException("Order number boş olamaz.", nameof(orderNumber));
        }

        if (warehouseId == Guid.Empty)
        {
            throw new ArgumentException("Order bir warehouse'a bağlı olmalıdır.", nameof(warehouseId));
        }

        if (lineSpecs.Count == 0)
        {
            throw new ArgumentException("Order en az bir line içermelidir.", nameof(lineSpecs));
        }

        var order = new FulfillmentOrder(
            requestId,
            orderNumber.Trim().ToUpperInvariant(),
            warehouseId,
            string.IsNullOrWhiteSpace(externalOrderReference) ? null : externalOrderReference.Trim());

        foreach (var spec in lineSpecs)
        {
            order._lines.Add(FulfillmentOrderLine.Create(order.Id, spec.SkuId, spec.RequestedQuantity));
        }

        return order;
    }

    public FulfillmentOrderLine GetLine(Guid lineId) =>
        _lines.FirstOrDefault(l => l.Id == lineId)
        ?? throw new InvalidOperationException($"Order line bulunamadı: {lineId}");

    public FulfillmentOrderLine GetLineBySku(Guid skuId) =>
        _lines.FirstOrDefault(l => l.SkuId == skuId)
        ?? throw new InvalidOperationException($"Order line bulunamadı: sku={skuId}");

    public void MarkAllocationFailed()
    {
        if (Status is not (OrderStatus.Created or OrderStatus.AllocationFailed))
        {
            throw new InvalidOperationException($"Order {Status} durumundayken allocation fail işaretlenemez.");
        }

        Status = OrderStatus.AllocationFailed;
    }

    public void MarkAllocated(DateTime? at = null)
    {
        if (Status is not (OrderStatus.Created or OrderStatus.AllocationFailed))
        {
            throw new InvalidOperationException($"Order {Status} durumundayken allocate edilemez.");
        }

        Status = OrderStatus.Allocated;
        AllocatedAt = at ?? DateTime.UtcNow;
    }

    public void MarkPicking(DateTime? at = null)
    {
        if (Status == OrderStatus.Allocated)
        {
            Status = OrderStatus.Picking;
            PickingStartedAt = at ?? DateTime.UtcNow;
        }
    }

    public void MarkPicked(DateTime? at = null)
    {
        if (Status is not (OrderStatus.Picking or OrderStatus.Picked))
        {
            throw new InvalidOperationException($"Order {Status} durumundayken picked işaretlenemez.");
        }

        Status = OrderStatus.Picked;
    }

    public void MarkPickException()
    {
        if (Status is not (OrderStatus.Allocated or OrderStatus.Picking or OrderStatus.Picked or OrderStatus.PickException))
        {
            throw new InvalidOperationException($"Order {Status} durumundayken pick exception işaretlenemez.");
        }

        Status = OrderStatus.PickException;
    }

    public void MarkPacked(DateTime? at = null)
    {
        if (Status != OrderStatus.Picked)
        {
            throw new InvalidOperationException($"Order yalnızca PICKED durumundayken pack edilebilir. Mevcut: {Status}");
        }

        Status = OrderStatus.Packed;
        PackedAt = at ?? DateTime.UtcNow;
    }

    public void MarkShipped(DateTime? at = null)
    {
        if (Status == OrderStatus.Shipped)
        {
            return;
        }

        if (Status != OrderStatus.Packed)
        {
            throw new InvalidOperationException($"Order yalnızca PACKED durumundayken ship edilebilir. Mevcut: {Status}");
        }

        Status = OrderStatus.Shipped;
        ShippedAt = at ?? DateTime.UtcNow;
    }

    public void Cancel(DateTime? at = null)
    {
        if (Status == OrderStatus.Cancelled)
        {
            return;
        }

        if (Status == OrderStatus.Shipped)
        {
            throw new InvalidOperationException(
                "Shipped order normal cancel edilemez — bu Return / reverse logistics alanıdır.");
        }

        Status = OrderStatus.Cancelled;
        CancelledAt = at ?? DateTime.UtcNow;
    }
}
