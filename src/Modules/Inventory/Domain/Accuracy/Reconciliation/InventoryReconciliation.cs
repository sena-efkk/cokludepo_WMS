using Wms.Modules.Inventory.Domain;

namespace Wms.Modules.Inventory.Domain.Accuracy.Reconciliation;

public sealed class InventoryReconciliation
{
    private InventoryReconciliation()
    {
        ResolutionNote = string.Empty;
    }

    private InventoryReconciliation(
        Guid cycleCountTaskId,
        Guid cycleCountResultId,
        Guid skuId,
        Guid warehouseId,
        Guid locationId,
        InventoryStatus status,
        int expectedQuantity,
        int countedQuantity,
        int variance,
        AdjustmentReason reason,
        bool isLargeVariance)
    {
        Id = Guid.NewGuid();
        CycleCountTaskId = cycleCountTaskId;
        CycleCountResultId = cycleCountResultId;
        SkuId = skuId;
        WarehouseId = warehouseId;
        LocationId = locationId;
        Status = status;
        ExpectedQuantity = expectedQuantity;
        CountedQuantity = countedQuantity;
        Variance = variance;
        Reason = reason;
        IsLargeVariance = isLargeVariance;
        ReconciliationStatus = ReconciliationStatus.Open;
        CreatedAt = DateTime.UtcNow;
        ResolutionNote = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid CycleCountTaskId { get; private set; }

    public Guid CycleCountResultId { get; private set; }

    public Guid SkuId { get; private set; }

    public Guid WarehouseId { get; private set; }

    public Guid LocationId { get; private set; }

    public InventoryStatus Status { get; private set; }

    public int ExpectedQuantity { get; private set; }

    public int CountedQuantity { get; private set; }

    public int Variance { get; private set; }

    public AdjustmentReason Reason { get; private set; }

    public bool IsLargeVariance { get; private set; }

    public ReconciliationStatus ReconciliationStatus { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime? ResolvedAt { get; private set; }

    public string? ResolvedBy { get; private set; }

    public string ResolutionNote { get; private set; }

    public static InventoryReconciliation Create(
        Guid cycleCountTaskId,
        Guid cycleCountResultId,
        Guid skuId,
        Guid warehouseId,
        Guid locationId,
        InventoryStatus status,
        int expectedQuantity,
        int countedQuantity,
        int variance,
        AdjustmentReason reason,
        bool isLargeVariance)
    {
        if (cycleCountTaskId == Guid.Empty || cycleCountResultId == Guid.Empty)
        {
            throw new ArgumentException("Reconciliation bir cycle count task/result'a bağlı olmalıdır.");
        }

        if (skuId == Guid.Empty || warehouseId == Guid.Empty || locationId == Guid.Empty)
        {
            throw new ArgumentException("Reconciliation; SKU, warehouse ve location zorunludur.");
        }

        if (expectedQuantity < 0 || countedQuantity < 0)
        {
            throw new ArgumentException("Miktarlar negatif olamaz.");
        }

        if (variance == 0)
        {
            throw new ArgumentException("Variance = 0 için reconciliation gerekmez.", nameof(variance));
        }

        return new InventoryReconciliation(
            cycleCountTaskId,
            cycleCountResultId,
            skuId,
            warehouseId,
            locationId,
            status,
            expectedQuantity,
            countedQuantity,
            variance,
            reason,
            isLargeVariance);
    }

    public void Approve(string? resolvedBy, string? resolutionNote)
    {
        EnsureOpen();
        ResolvedAt = DateTime.UtcNow;
        ResolvedBy = resolvedBy;
        ResolutionNote = resolutionNote ?? string.Empty;
        ReconciliationStatus = ReconciliationStatus.Approved;
    }

    public void Reject(string? resolvedBy, string? resolutionNote)
    {
        EnsureOpen();
        ResolvedAt = DateTime.UtcNow;
        ResolvedBy = resolvedBy;
        ResolutionNote = resolutionNote ?? string.Empty;
        ReconciliationStatus = ReconciliationStatus.Rejected;
    }

    public void Cancel(string? resolvedBy, string? resolutionNote)
    {
        EnsureOpen();
        ResolvedAt = DateTime.UtcNow;
        ResolvedBy = resolvedBy;
        ResolutionNote = resolutionNote ?? string.Empty;
        ReconciliationStatus = ReconciliationStatus.Cancelled;
    }

    private void EnsureOpen()
    {
        if (ReconciliationStatus != ReconciliationStatus.Open)
        {
            throw new InvalidOperationException($"Yalnızca OPEN reconciliation işlenebilir. Mevcut: {ReconciliationStatus}.");
        }
    }
}
