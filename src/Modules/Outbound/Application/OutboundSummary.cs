using Wms.Modules.Outbound.Application;
using Wms.Modules.Outbound.Domain;

namespace Wms.Modules.Outbound.Application;

public sealed record OutboundSummary(
    int OpenOrders,
    int AllocatedOrders,
    int PickingOrders,
    int PendingPickTasks,
    int PendingShipments);

public sealed class GetOutboundSummary(IOutboundStore store)
{
    public async Task<OutboundSummary> Handle(Guid? warehouseId, CancellationToken cancellationToken)
    {
        var orders = await store.ListOrdersAsync(warehouseId, 10_000, cancellationToken);
        var open = orders.Count(o => o.Status == OrderStatus.Created);
        var allocated = orders.Count(o => o.Status == OrderStatus.Allocated);
        var picking = orders.Count(o => o.Status is OrderStatus.Picking or OrderStatus.Picked or OrderStatus.PickException);

        var tasks = await store.ListPickTasksAsync(warehouseId, null, 10_000, cancellationToken);
        var pendingPicks = tasks.Count(t => t.Status is PickTaskStatus.Pending or PickTaskStatus.InProgress);

        return new OutboundSummary(open, allocated, picking, pendingPicks, 0);
    }
}
