using Wms.Modules.Inventory.Domain.Accuracy;

namespace Wms.Modules.Inventory.Application.Accuracy;

public sealed record AccuracySignalFilter(
    Guid? WarehouseId,
    Guid? SkuId,
    Guid? LocationId,
    AccuracySignalType? SignalType,
    DateTime? From,
    DateTime? To);

public sealed class GetAccuracySignals(IInventoryStore store)
{
    public async Task<IReadOnlyList<InventoryAccuracySignal>> Handle(
        AccuracySignalFilter filter,
        int limit,
        CancellationToken cancellationToken)
    {
        return await store.ListAccuracySignalsAsync(
            filter.WarehouseId,
            filter.SkuId,
            filter.LocationId,
            filter.SignalType,
            filter.From,
            filter.To,
            limit,
            cancellationToken);
    }
}

public sealed class GetSignalsForSkuLocation(IInventoryStore store)
{
    public async Task<IReadOnlyList<InventoryAccuracySignal>> Handle(
        Guid warehouseId,
        Guid skuId,
        Guid locationId,
        AccuracySignalType? signalType,
        DateTime? from,
        DateTime? to,
        CancellationToken cancellationToken)
    {
        return await store.ListAccuracySignalsAsync(
            warehouseId,
            skuId,
            locationId,
            signalType,
            from,
            to,
            500,
            cancellationToken);
    }
}

public sealed class GetRecentNotFoundSignals(IInventoryStore store)
{
    public async Task<IReadOnlyList<InventoryAccuracySignal>> Handle(
        Guid? warehouseId,
        int days,
        int limit,
        CancellationToken cancellationToken)
    {
        var from = DateTime.UtcNow.AddDays(-days);
        return await store.ListAccuracySignalsAsync(
            warehouseId,
            null,
            null,
            AccuracySignalType.PickNotFound,
            from,
            null,
            limit,
            cancellationToken);
    }
}
