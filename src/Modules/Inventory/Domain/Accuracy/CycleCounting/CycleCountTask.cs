using Wms.Modules.Inventory.Domain;

namespace Wms.Modules.Inventory.Domain.Accuracy.CycleCounting;

public sealed class CycleCountTask
{
    private CycleCountTask()
    {
        Evidence = string.Empty;
    }

    private CycleCountTask(
        Guid warehouseId,
        Guid locationId,
        Guid skuId,
        CycleCountReason reason,
        CycleCountPriority priority,
        int riskScoreAtCreation,
        string evidence)
    {
        Id = Guid.NewGuid();
        WarehouseId = warehouseId;
        LocationId = locationId;
        SkuId = skuId;
        Reason = reason;
        Priority = priority;
        RiskScoreAtCreation = riskScoreAtCreation;
        Evidence = evidence;
        Status = CycleCountTaskStatus.Pending;
        CreatedAt = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid WarehouseId { get; private set; }

    public Guid LocationId { get; private set; }

    public Guid SkuId { get; private set; }

    public CycleCountReason Reason { get; private set; }

    public CycleCountPriority Priority { get; private set; }

    public int RiskScoreAtCreation { get; private set; }

    public string Evidence { get; private set; }

    public CycleCountTaskStatus Status { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime? DueAt { get; private set; }

    public string? AssignedTo { get; private set; }

    public DateTime? StartedAt { get; private set; }

    public DateTime? CompletedAt { get; private set; }

    public int? ExpectedQuantity { get; private set; }

    public int? ExpectedAllocated { get; private set; }

    public InventoryStatus? ExpectedStatus { get; private set; }

    public static CycleCountTask Create(
        Guid warehouseId,
        Guid locationId,
        Guid skuId,
        CycleCountReason reason,
        CycleCountPriority priority,
        int riskScoreAtCreation,
        string evidence)
    {
        if (warehouseId == Guid.Empty || locationId == Guid.Empty || skuId == Guid.Empty)
        {
            throw new ArgumentException("Cycle count task; warehouse, location ve SKU zorunludur.");
        }

        if (riskScoreAtCreation < 0)
        {
            throw new ArgumentException("RiskScoreAtCreation negatif olamaz.", nameof(riskScoreAtCreation));
        }

        if (string.IsNullOrWhiteSpace(evidence))
        {
            throw new ArgumentException("Görevin neden üretildiği (evidence) kaydedilmelidir.", nameof(evidence));
        }

        return new CycleCountTask(warehouseId, locationId, skuId, reason, priority, riskScoreAtCreation, evidence.Trim());
    }

    public void Start(int expectedQuantity, int expectedAllocated, InventoryStatus expectedStatus, string? assignedTo)
    {
        if (Status != CycleCountTaskStatus.Pending)
        {
            throw new InvalidOperationException($"Yalnızca PENDING task başlatılabilir. Mevcut: {Status}.");
        }

        if (expectedQuantity < 0 || expectedAllocated < 0)
        {
            throw new ArgumentException("Expected snapshot negatif olamaz.");
        }

        ExpectedQuantity = expectedQuantity;
        ExpectedAllocated = expectedAllocated;
        ExpectedStatus = expectedStatus;
        AssignedTo = assignedTo;
        StartedAt = DateTime.UtcNow;
        Status = CycleCountTaskStatus.InProgress;
    }

    public void Cancel()
    {
        if (Status != CycleCountTaskStatus.Pending)
        {
            throw new InvalidOperationException($"Yalnızca PENDING task iptal edilebilir. Mevcut: {Status}.");
        }

        Status = CycleCountTaskStatus.Cancelled;
    }

    public void Complete()
    {
        if (Status != CycleCountTaskStatus.InProgress)
        {
            throw new InvalidOperationException($"Yalnızca IN_PROGRESS task tamamlanabilir. Mevcut: {Status}.");
        }

        CompletedAt = DateTime.UtcNow;
        Status = CycleCountTaskStatus.Completed;
    }
}
