namespace Wms.Modules.Inventory.Domain;

public sealed class InventoryLedgerEntry
{
    private InventoryLedgerEntry()
    {
    }

    private InventoryLedgerEntry(
        Guid requestId,
        Guid skuId,
        Guid warehouseId,
        Guid locationId,
        InventoryStatus status,
        LedgerEntryType entryType,
        int quantityDelta,
        int allocatedDelta,
        Guid? movementId,
        string? referenceType,
        Guid? referenceId)
    {
        Id = Guid.NewGuid();
        RequestId = requestId;
        SkuId = skuId;
        WarehouseId = warehouseId;
        LocationId = locationId;
        Status = status;
        EntryType = entryType;
        QuantityDelta = quantityDelta;
        AllocatedDelta = allocatedDelta;
        MovementId = movementId;
        ReferenceType = referenceType;
        ReferenceId = referenceId;
        OccurredAt = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid RequestId { get; private set; }

    public Guid SkuId { get; private set; }

    public Guid WarehouseId { get; private set; }

    public Guid LocationId { get; private set; }

    public InventoryStatus Status { get; private set; }

    public LedgerEntryType EntryType { get; private set; }

    public int QuantityDelta { get; private set; }

    public int AllocatedDelta { get; private set; }

    public Guid? MovementId { get; private set; }

    public string? ReferenceType { get; private set; }

    public Guid? ReferenceId { get; private set; }

    public DateTime OccurredAt { get; private set; }

    public static InventoryLedgerEntry Create(
        Guid requestId,
        Guid skuId,
        Guid warehouseId,
        Guid locationId,
        InventoryStatus status,
        LedgerEntryType entryType,
        int quantityDelta,
        int allocatedDelta,
        Guid? movementId = null,
        string? referenceType = null,
        Guid? referenceId = null)
    {
        if (requestId == Guid.Empty)
        {
            throw new ArgumentException("Ledger entry bir RequestId taşımalıdır.", nameof(requestId));
        }

        return new InventoryLedgerEntry(
            requestId,
            skuId,
            warehouseId,
            locationId,
            status,
            entryType,
            quantityDelta,
            allocatedDelta,
            movementId,
            string.IsNullOrWhiteSpace(referenceType) ? null : referenceType.Trim(),
            referenceId);
    }
}
