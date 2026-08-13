using Wms.Modules.Inventory.Domain;

namespace Wms.Modules.Inventory.Domain.Accuracy;

public sealed class InventoryAccuracySignal
{
    private InventoryAccuracySignal()
    {
    }

    private InventoryAccuracySignal(
        Guid requestId,
        AccuracySignalType signalType,
        AccuracySourceType sourceType,
        Guid skuId,
        Guid warehouseId,
        Guid locationId,
        Guid? sourceReferenceId,
        DateTime occurredAt,
        int systemQuantityAtSignal,
        int allocatedAtSignal,
        int availableAtSignal,
        InventoryStatus statusAtSignal)
    {
        Id = Guid.NewGuid();
        RequestId = requestId;
        SignalType = signalType;
        SourceType = sourceType;
        SkuId = skuId;
        WarehouseId = warehouseId;
        LocationId = locationId;
        SourceReferenceId = sourceReferenceId;
        OccurredAt = occurredAt;
        RecordedAt = DateTime.UtcNow;
        SystemQuantityAtSignal = systemQuantityAtSignal;
        AllocatedAtSignal = allocatedAtSignal;
        AvailableAtSignal = availableAtSignal;
        StatusAtSignal = statusAtSignal;
    }

    public Guid Id { get; private set; }

    public Guid RequestId { get; private set; }

    public AccuracySignalType SignalType { get; private set; }

    public AccuracySourceType SourceType { get; private set; }

    public Guid SkuId { get; private set; }

    public Guid WarehouseId { get; private set; }

    public Guid LocationId { get; private set; }

    public Guid? SourceReferenceId { get; private set; }

    public DateTime OccurredAt { get; private set; }

    public DateTime RecordedAt { get; private set; }

    public int SystemQuantityAtSignal { get; private set; }

    public int AllocatedAtSignal { get; private set; }

    public int AvailableAtSignal { get; private set; }

    public InventoryStatus StatusAtSignal { get; private set; }

    public static InventoryAccuracySignal CreatePickNotFound(
        Guid requestId,
        AccuracySourceType sourceType,
        Guid skuId,
        Guid warehouseId,
        Guid locationId,
        Guid? sourceReferenceId,
        DateTime occurredAt,
        int systemQuantityAtSignal,
        int allocatedAtSignal,
        int availableAtSignal,
        InventoryStatus statusAtSignal)
    {
        return Create(
            requestId,
            AccuracySignalType.PickNotFound,
            sourceType,
            skuId,
            warehouseId,
            locationId,
            sourceReferenceId,
            occurredAt,
            systemQuantityAtSignal,
            allocatedAtSignal,
            availableAtSignal,
            statusAtSignal);
    }

    public static InventoryAccuracySignal CreateDiscrepancyConfirmed(
        Guid requestId,
        AccuracySourceType sourceType,
        Guid skuId,
        Guid warehouseId,
        Guid locationId,
        Guid sourceReferenceId,
        DateTime occurredAt,
        int systemQuantityAtSignal,
        int allocatedAtSignal,
        int availableAtSignal,
        InventoryStatus statusAtSignal)
    {
        return Create(
            requestId,
            AccuracySignalType.DiscrepancyConfirmed,
            sourceType,
            skuId,
            warehouseId,
            locationId,
            sourceReferenceId,
            occurredAt,
            systemQuantityAtSignal,
            allocatedAtSignal,
            availableAtSignal,
            statusAtSignal);
    }

    private static InventoryAccuracySignal Create(
        Guid requestId,
        AccuracySignalType signalType,
        AccuracySourceType sourceType,
        Guid skuId,
        Guid warehouseId,
        Guid locationId,
        Guid? sourceReferenceId,
        DateTime occurredAt,
        int systemQuantityAtSignal,
        int allocatedAtSignal,
        int availableAtSignal,
        InventoryStatus statusAtSignal)
    {
        if (requestId == Guid.Empty)
        {
            throw new ArgumentException("Signal bir RequestId taşımalıdır.", nameof(requestId));
        }

        if (skuId == Guid.Empty)
        {
            throw new ArgumentException("Signal bir SKU'ya bağlı olmalıdır.", nameof(skuId));
        }

        if (warehouseId == Guid.Empty)
        {
            throw new ArgumentException("Signal bir Warehouse'a bağlı olmalıdır.", nameof(warehouseId));
        }

        if (locationId == Guid.Empty)
        {
            throw new ArgumentException("Signal bir Location'a bağlı olmalıdır.", nameof(locationId));
        }

        if (systemQuantityAtSignal < 0 || allocatedAtSignal < 0 || availableAtSignal < 0)
        {
            throw new ArgumentException("Signal snapshot miktarları negatif olamaz.");
        }

        return new InventoryAccuracySignal(
            requestId,
            signalType,
            sourceType,
            skuId,
            warehouseId,
            locationId,
            sourceReferenceId,
            occurredAt == default ? DateTime.UtcNow : occurredAt,
            systemQuantityAtSignal,
            allocatedAtSignal,
            availableAtSignal,
            statusAtSignal);
    }
}
