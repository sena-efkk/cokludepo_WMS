using Wms.Modules.Inbound.Domain;

namespace Wms.Modules.Inbound.Application;

public sealed record PutawayTaskQuery(
    Guid Id,
    Guid ReceiptId,
    Guid ReceiptLineId,
    Guid ReceiveRecordId,
    Guid SkuId,
    Guid WarehouseId,
    Guid SourceLocationId,
    string InventoryStatus,
    int Quantity,
    PutawayTaskStatus Status,
    Guid? MovementId,
    DateTime CreatedAt,
    DateTime? StartedAt,
    DateTime? CompletedAt)
{
    public static PutawayTaskQuery From(PutawayTask task) =>
        new(
            task.Id,
            task.ReceiptId,
            task.ReceiptLineId,
            task.ReceiveRecordId,
            task.SkuId,
            task.WarehouseId,
            task.SourceLocationId,
            task.InventoryStatus,
            task.Quantity,
            task.Status,
            task.MovementId,
            task.CreatedAt,
            task.StartedAt,
            task.CompletedAt);
}

public sealed class GetPutawayTask(IInboundStore store)
{
    public async Task<PutawayTaskQuery?> Handle(Guid taskId, CancellationToken cancellationToken)
    {
        var task = await store.GetPutawayTaskAsync(taskId, cancellationToken);
        return task is null ? null : PutawayTaskQuery.From(task);
    }
}

public sealed class ListPutawayTasks(IInboundStore store)
{
    public async Task<IReadOnlyList<PutawayTaskQuery>> Handle(
        Guid? warehouseId,
        PutawayTaskStatus? status,
        int limit,
        CancellationToken cancellationToken)
    {
        var tasks = await store.ListPutawayTasksAsync(warehouseId, status, limit, cancellationToken);
        return tasks.Select(PutawayTaskQuery.From).ToList();
    }
}
