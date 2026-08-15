using Wms.Modules.Outbound.Domain;

namespace Wms.Modules.Outbound.Application;

public sealed record OrderQueryLine(
    Guid Id,
    Guid SkuId,
    int RequestedQuantity,
    Guid? ReservationId);

public sealed record PickTaskQuery(
    Guid Id,
    Guid OrderId,
    Guid OrderLineId,
    Guid ReservationId,
    Guid ReservationLineId,
    Guid WarehouseId,
    Guid LocationId,
    Guid SkuId,
    int RequiredQuantity,
    int PickedQuantity,
    PickTaskStatus Status,
    DateTime CreatedAt,
    DateTime? StartedAt,
    DateTime? CompletedAt)
{
    public static PickTaskQuery From(PickTask task) =>
        new(
            task.Id,
            task.OrderId,
            task.OrderLineId,
            task.ReservationId,
            task.ReservationLineId,
            task.WarehouseId,
            task.LocationId,
            task.SkuId,
            task.RequiredQuantity,
            task.PickedQuantity,
            task.Status,
            task.CreatedAt,
            task.StartedAt,
            task.CompletedAt);
}

public sealed record OrderQuery(
    Guid Id,
    Guid RequestId,
    string OrderNumber,
    Guid WarehouseId,
    string? ExternalOrderReference,
    OrderStatus Status,
    DateTime CreatedAt,
    DateTime? AllocatedAt,
    DateTime? PickingStartedAt,
    DateTime? PackedAt,
    DateTime? ShippedAt,
    DateTime? CancelledAt,
    IReadOnlyList<OrderQueryLine> Lines,
    IReadOnlyList<PickTaskQuery> PickTasks,
    Package? Package,
    Shipment? Shipment);

public sealed record OrderSummary(
    Guid Id,
    string OrderNumber,
    Guid WarehouseId,
    string? ExternalOrderReference,
    OrderStatus Status,
    DateTime CreatedAt,
    int TotalRequested);

public sealed class GetOrder(IOutboundStore store)
{
    public async Task<OrderQuery?> Handle(Guid orderId, CancellationToken cancellationToken)
    {
        var order = await store.GetOrderAsync(orderId, cancellationToken);
        if (order is null)
        {
            return null;
        }

        var tasks = await store.ListPickTasksByOrderAsync(orderId, cancellationToken);
        var package = await store.GetPackageByOrderAsync(orderId, cancellationToken);
        var shipment = await store.GetShipmentByOrderAsync(orderId, cancellationToken);

        return new OrderQuery(
            order.Id,
            order.RequestId,
            order.OrderNumber,
            order.WarehouseId,
            order.ExternalOrderReference,
            order.Status,
            order.CreatedAt,
            order.AllocatedAt,
            order.PickingStartedAt,
            order.PackedAt,
            order.ShippedAt,
            order.CancelledAt,
            order.Lines
                .Select(l => new OrderQueryLine(l.Id, l.SkuId, l.RequestedQuantity, l.ReservationId))
                .ToList(),
            tasks.Select(PickTaskQuery.From).ToList(),
            package,
            shipment);
    }
}

public sealed class ListOrders(IOutboundStore store)
{
    public async Task<IReadOnlyList<OrderSummary>> Handle(Guid? warehouseId, int limit, CancellationToken cancellationToken)
    {
        var orders = await store.ListOrdersAsync(warehouseId, limit, cancellationToken);
        return orders
            .Select(o => new OrderSummary(
                o.Id,
                o.OrderNumber,
                o.WarehouseId,
                o.ExternalOrderReference,
                o.Status,
                o.CreatedAt,
                o.Lines.Sum(l => l.RequestedQuantity)))
            .ToList();
    }
}

public sealed class GetPickTask(IOutboundStore store)
{
    public async Task<PickTaskQuery?> Handle(Guid taskId, CancellationToken cancellationToken)
    {
        var task = await store.GetPickTaskAsync(taskId, cancellationToken);
        return task is null ? null : PickTaskQuery.From(task);
    }
}

public sealed class ListPickTasks(IOutboundStore store)
{
    public async Task<IReadOnlyList<PickTaskQuery>> Handle(
        Guid? warehouseId,
        PickTaskStatus? status,
        int limit,
        CancellationToken cancellationToken)
    {
        var tasks = await store.ListPickTasksAsync(warehouseId, status, limit, cancellationToken);
        return tasks.Select(PickTaskQuery.From).ToList();
    }
}
