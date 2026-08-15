using Wms.Modules.Inventory.Contracts;
using Wms.Integration.Telemetry;
using Wms.Modules.Outbound.Domain;

namespace Wms.Modules.Outbound.Application;

public sealed record MarkPickNotFoundCommand(Guid TaskId, Guid RequestId);

public sealed record MarkPickNotFoundResult(Guid TaskId, bool OrderPickException, Guid? SignalRequestId);

public sealed class MarkPickNotFound(
    IOutboundStore store,
    IInventoryContract inventory)
{
    public async Task<MarkPickNotFoundResult> Handle(MarkPickNotFoundCommand command, CancellationToken cancellationToken)
    {
        var task = await store.GetPickTaskAsync(command.TaskId, cancellationToken)
            ?? throw new PickTaskNotFoundException(command.TaskId);

        if (task.Status == PickTaskStatus.NotFound)
        {
            return new MarkPickNotFoundResult(task.Id, false, null);
        }

        if (task.Status is PickTaskStatus.Completed or PickTaskStatus.Cancelled)
        {
            throw new InvalidPickTaskStateException($"Pick task {task.Status} durumundayken NotFound işaretlenemez.");
        }

        // Sinyal snapshot'ını Inventory kendi authoritative balance'ından alır —
        // Outbound quantity uydurmaz.
        await inventory.ReportPickNotFoundAsync(
            command.RequestId,
            task.SkuId,
            task.WarehouseId,
            task.LocationId,
            task.Id.ToString(),
            cancellationToken);

        await store.BeginTransactionAsync(cancellationToken);
        try
        {
            task.MarkNotFound();

            var order = await store.GetOrderAsync(task.OrderId, cancellationToken)
                ?? throw new OrderNotFoundException(task.OrderId);
            order.MarkPickException();

            await store.SaveChangesAsync(cancellationToken);
            await store.CommitTransactionAsync(cancellationToken);
            WmsMetrics.PickFailuresTotal.Add(1);

            return new MarkPickNotFoundResult(task.Id, true, command.RequestId);
        }
        catch
        {
            await store.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }
}
