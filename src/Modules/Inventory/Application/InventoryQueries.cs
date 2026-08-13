using Wms.Modules.Inventory.Domain;

namespace Wms.Modules.Inventory.Application;

public sealed record StatusQuantity(InventoryStatus Status, int Quantity);

public sealed record WarehouseSkuSummary(
    Guid SkuId,
    Guid WarehouseId,
    int OnHand,
    int Allocated,
    int Available,
    IReadOnlyList<StatusQuantity> ByStatus);

public sealed record BalanceView(
    Guid SkuId,
    Guid WarehouseId,
    Guid LocationId,
    InventoryStatus Status,
    int Quantity,
    int Allocated,
    int Available);

public sealed class GetWarehouseBalances(IInventoryStore store)
{
    public async Task<IReadOnlyList<BalanceView>> Handle(
        Guid warehouseId,
        Guid? skuId,
        Guid? locationId,
        bool includeEmpty,
        CancellationToken cancellationToken)
    {
        var balances = await store.ListBalancesAsync(warehouseId, skuId, locationId, includeEmpty, cancellationToken);
        return balances
            .Select(b => new BalanceView(b.SkuId, b.WarehouseId, b.LocationId, b.Status, b.Quantity, b.Allocated, b.Available))
            .ToList();
    }
}

public sealed class GetWarehouseSkuSummary(IInventoryStore store)
{
    public async Task<WarehouseSkuSummary> Handle(Guid warehouseId, Guid skuId, CancellationToken cancellationToken)
    {
        var balances = await store.ListBalancesAsync(warehouseId, skuId, null, includeEmpty: true, cancellationToken);

        var byStatus = balances
            .GroupBy(b => b.Status)
            .Select(g => new StatusQuantity(g.Key, g.Sum(b => b.Quantity)))
            .OrderBy(s => s.Status)
            .ToList();

        var onHand = balances.Sum(b => b.Quantity);
        var allocated = balances.Sum(b => b.Allocated);
        var available = balances
            .Where(b => b.Status == InventoryStatus.Available)
            .Sum(b => b.Quantity - b.Allocated);

        return new WarehouseSkuSummary(skuId, warehouseId, onHand, allocated, available, byStatus);
    }
}

public sealed class GetReservation(IInventoryStore store)
{
    public async Task<InventoryReservation?> Handle(Guid reservationId, CancellationToken cancellationToken)
    {
        return await store.GetReservationAsync(reservationId, cancellationToken);
    }
}

public sealed class GetLedger(IInventoryStore store)
{
    public async Task<IReadOnlyList<InventoryLedgerEntry>> Handle(
        Guid? warehouseId,
        Guid? skuId,
        Guid? locationId,
        int limit,
        CancellationToken cancellationToken)
    {
        return await store.ListLedgerAsync(warehouseId, skuId, locationId, limit, cancellationToken);
    }
}
