namespace Wms.Modules.Inventory.Application;

public sealed record SkuWarehouseAvailabilityView(
    Guid SkuId,
    Guid WarehouseId,
    int PhysicalStock,
    int Allocated,
    int AvailableQuantity,
    int Hold,
    int Quarantine,
    int Damaged);

public sealed record SkuLocationBalanceView(
    Guid LocationId,
    string Status,
    int Quantity,
    int Allocated,
    int Available);

public sealed record WarehouseStockRollupView(
    Guid WarehouseId,
    int SkuCount,
    int PhysicalStock,
    int Allocated,
    int AvailableQuantity,
    int Hold,
    int Quarantine,
    int Damaged);

public sealed record WarehouseSkuStockRowView(
    Guid SkuId,
    int PhysicalStock,
    int Allocated,
    int AvailableQuantity,
    int Hold,
    int Quarantine,
    int Damaged);
