using Wms.Modules.Inventory.Domain;

namespace Wms.Modules.Inventory.Domain.Accuracy.Reconciliation;

public sealed class InventoryAdjustment
{
    private InventoryAdjustment()
    {
        ResolutionNote = string.Empty;
    }

    private InventoryAdjustment(
        Guid reconciliationId,
        Guid requestId,
        Guid skuId,
        Guid warehouseId,
        Guid locationId,
        InventoryStatus status,
        int quantityDelta,
        AdjustmentReason reason,
        string? resolvedBy,
        string? resolutionNote)
    {
        Id = Guid.NewGuid();
        ReconciliationId = reconciliationId;
        RequestId = requestId;
        SkuId = skuId;
        WarehouseId = warehouseId;
        LocationId = locationId;
        Status = status;
        QuantityDelta = quantityDelta;
        Reason = reason;
        ResolvedBy = resolvedBy;
        ResolutionNote = resolutionNote ?? string.Empty;
        ResolvedAt = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid ReconciliationId { get; private set; }

    public Guid RequestId { get; private set; }

    public Guid SkuId { get; private set; }

    public Guid WarehouseId { get; private set; }

    public Guid LocationId { get; private set; }

    public InventoryStatus Status { get; private set; }

    public int QuantityDelta { get; private set; }

    public AdjustmentReason Reason { get; private set; }

    public string? ResolvedBy { get; private set; }

    public string ResolutionNote { get; private set; }

    public DateTime ResolvedAt { get; private set; }

    public static InventoryAdjustment Create(
        Guid reconciliationId,
        Guid requestId,
        Guid skuId,
        Guid warehouseId,
        Guid locationId,
        InventoryStatus status,
        int quantityDelta,
        AdjustmentReason reason,
        string? resolvedBy,
        string? resolutionNote)
    {
        if (reconciliationId == Guid.Empty || requestId == Guid.Empty)
        {
            throw new ArgumentException("Adjustment; reconciliation ve request zorunludur.");
        }

        if (quantityDelta == 0)
        {
            throw new ArgumentException("Adjustment deltası sıfır olamaz.", nameof(quantityDelta));
        }

        return new InventoryAdjustment(
            reconciliationId,
            requestId,
            skuId,
            warehouseId,
            locationId,
            status,
            quantityDelta,
            reason,
            resolvedBy,
            resolutionNote);
    }
}
