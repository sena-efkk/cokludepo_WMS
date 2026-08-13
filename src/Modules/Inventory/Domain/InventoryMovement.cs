namespace Wms.Modules.Inventory.Domain;

public sealed class InventoryMovement
{
    private InventoryMovement()
    {
    }

    private InventoryMovement(
        Guid requestId,
        MovementType type,
        Guid skuId,
        Guid warehouseId,
        Guid sourceLocationId,
        Guid? destinationLocationId,
        InventoryStatus statusFrom,
        InventoryStatus statusTo,
        int quantity)
    {
        Id = Guid.NewGuid();
        RequestId = requestId;
        Type = type;
        SkuId = skuId;
        WarehouseId = warehouseId;
        SourceLocationId = sourceLocationId;
        DestinationLocationId = destinationLocationId;
        StatusFrom = statusFrom;
        StatusTo = statusTo;
        Quantity = quantity;
        OccurredAt = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid RequestId { get; private set; }

    public MovementType Type { get; private set; }

    public Guid SkuId { get; private set; }

    public Guid WarehouseId { get; private set; }

    public Guid SourceLocationId { get; private set; }

    public Guid? DestinationLocationId { get; private set; }

    public InventoryStatus StatusFrom { get; private set; }

    public InventoryStatus StatusTo { get; private set; }

    public int Quantity { get; private set; }

    public DateTime OccurredAt { get; private set; }

    public static InventoryMovement CreateRelocate(
        Guid requestId,
        Guid skuId,
        Guid warehouseId,
        Guid sourceLocationId,
        Guid destinationLocationId,
        int quantity)
    {
        ValidateCommon(requestId, skuId, warehouseId, quantity);

        if (sourceLocationId == Guid.Empty || destinationLocationId == Guid.Empty)
        {
            throw new ArgumentException("Relocate için source ve destination location zorunludur.");
        }

        if (sourceLocationId == destinationLocationId)
        {
            throw new ArgumentException("Relocate'te source ve destination aynı location olamaz.");
        }

        return new InventoryMovement(
            requestId,
            MovementType.Relocate,
            skuId,
            warehouseId,
            sourceLocationId,
            destinationLocationId,
            InventoryStatus.Available,
            InventoryStatus.Available,
            quantity);
    }

    public static InventoryMovement CreateStatusChange(
        Guid requestId,
        Guid skuId,
        Guid warehouseId,
        Guid locationId,
        InventoryStatus fromStatus,
        InventoryStatus toStatus,
        int quantity)
    {
        ValidateCommon(requestId, skuId, warehouseId, quantity);

        if (locationId == Guid.Empty)
        {
            throw new ArgumentException("Status change için location zorunludur.");
        }

        if (fromStatus == toStatus)
        {
            throw new ArgumentException("Status change'de from ve to status aynı olamaz.");
        }

        return new InventoryMovement(
            requestId,
            MovementType.StatusChange,
            skuId,
            warehouseId,
            locationId,
            locationId,
            fromStatus,
            toStatus,
            quantity);
    }

    private static void ValidateCommon(Guid requestId, Guid skuId, Guid warehouseId, int quantity)
    {
        if (requestId == Guid.Empty)
        {
            throw new ArgumentException("Movement bir RequestId taşımalıdır.", nameof(requestId));
        }

        if (skuId == Guid.Empty)
        {
            throw new ArgumentException("Movement bir SKU'ya bağlı olmalıdır.", nameof(skuId));
        }

        if (warehouseId == Guid.Empty)
        {
            throw new ArgumentException("Movement bir Warehouse'a bağlı olmalıdır.", nameof(warehouseId));
        }

        if (quantity <= 0)
        {
            throw new ArgumentException("Movement miktarı pozitif olmalıdır.", nameof(quantity));
        }
    }
}
