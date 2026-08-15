namespace Wms.Modules.Inbound.Domain;

public sealed class PutawayTask : IHasTimestamps
{
    private PutawayTask()
    {
        InventoryStatus = string.Empty;
    }

    private PutawayTask(
        Guid receiptId,
        Guid receiptLineId,
        Guid receiveRecordId,
        Guid skuId,
        Guid warehouseId,
        Guid sourceLocationId,
        string inventoryStatus,
        int quantity)
    {
        Id = Guid.NewGuid();
        ReceiptId = receiptId;
        ReceiptLineId = receiptLineId;
        ReceiveRecordId = receiveRecordId;
        SkuId = skuId;
        WarehouseId = warehouseId;
        SourceLocationId = sourceLocationId;
        InventoryStatus = inventoryStatus;
        Quantity = quantity;
        Status = PutawayTaskStatus.Pending;
        var now = DateTime.UtcNow;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid ReceiptId { get; private set; }

    public Guid ReceiptLineId { get; private set; }

    public Guid ReceiveRecordId { get; private set; }

    public Guid SkuId { get; private set; }

    public Guid WarehouseId { get; private set; }

    public Guid SourceLocationId { get; private set; }

    public string InventoryStatus { get; private set; }

    public int Quantity { get; private set; }

    public PutawayTaskStatus Status { get; private set; }

    public Guid? MovementId { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime? StartedAt { get; private set; }

    public DateTime? CompletedAt { get; private set; }

    public DateTime UpdatedAt { get; set; }

    public static PutawayTask Create(
        Guid receiptId,
        Guid receiptLineId,
        Guid receiveRecordId,
        Guid skuId,
        Guid warehouseId,
        Guid sourceLocationId,
        string inventoryStatus,
        int quantity)
    {
        if (receiptId == Guid.Empty || receiptLineId == Guid.Empty)
        {
            throw new ArgumentException("Putaway task bir receipt/line'a bağlı olmalıdır.");
        }

        if (receiveRecordId == Guid.Empty)
        {
            throw new ArgumentException("Putaway task bir receive record'dan üretilmelidir.", nameof(receiveRecordId));
        }

        if (skuId == Guid.Empty || warehouseId == Guid.Empty || sourceLocationId == Guid.Empty)
        {
            throw new ArgumentException("Putaway task; SKU, warehouse ve source location zorunludur.");
        }

        if (string.IsNullOrWhiteSpace(inventoryStatus))
        {
            throw new ArgumentException("Putaway task bir inventory status taşımalıdır.", nameof(inventoryStatus));
        }

        if (quantity <= 0)
        {
            throw new ArgumentException("Putaway task quantity pozitif olmalıdır.", nameof(quantity));
        }

        return new PutawayTask(
            receiptId,
            receiptLineId,
            receiveRecordId,
            skuId,
            warehouseId,
            sourceLocationId,
            inventoryStatus.Trim().ToUpperInvariant(),
            quantity);
    }

    public void Start(DateTime? at = null)
    {
        if (Status == PutawayTaskStatus.Completed)
        {
            throw new InvalidOperationException("Tamamlanmış putaway task başlatılamaz.");
        }

        if (Status == PutawayTaskStatus.Cancelled)
        {
            throw new InvalidOperationException("İptal edilmiş putaway task başlatılamaz.");
        }

        if (Status == PutawayTaskStatus.Pending)
        {
            Status = PutawayTaskStatus.InProgress;
            StartedAt = at ?? DateTime.UtcNow;
        }
    }

    public void Complete(Guid movementId, DateTime? at = null)
    {
        if (Status == PutawayTaskStatus.Completed)
        {
            return;
        }

        if (Status == PutawayTaskStatus.Cancelled)
        {
            throw new InvalidOperationException("İptal edilmiş putaway task tamamlanamaz.");
        }

        if (movementId == Guid.Empty)
        {
            throw new ArgumentException("Putaway completion bir movement'a bağlı olmalıdır.", nameof(movementId));
        }

        Status = PutawayTaskStatus.Completed;
        MovementId = movementId;
        CompletedAt = at ?? DateTime.UtcNow;
    }
}
