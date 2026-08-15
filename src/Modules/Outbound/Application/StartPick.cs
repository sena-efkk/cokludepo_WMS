using Wms.Modules.Outbound.Domain;

namespace Wms.Modules.Outbound.Application;

public sealed class StartPick(IOutboundStore store)
{
    public async Task<PickTask> Handle(Guid taskId, CancellationToken cancellationToken)
    {
        var task = await store.GetPickTaskAsync(taskId, cancellationToken)
            ?? throw new PickTaskNotFoundException(taskId);

        task.Start();

        var order = await store.GetOrderAsync(task.OrderId, cancellationToken)
            ?? throw new OrderNotFoundException(task.OrderId);
        order.MarkPicking();

        await store.SaveChangesAsync(cancellationToken);
        return task;
    }
}
