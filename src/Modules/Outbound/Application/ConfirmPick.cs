using Wms.Modules.Facility.Contracts;
using Wms.Modules.MasterData.Contracts;
using Wms.Modules.Outbound.Domain;

namespace Wms.Modules.Outbound.Application;

public sealed record ConfirmPickCommand(
    Guid TaskId,
    string LocationScan,
    string SkuScan,
    int Quantity);

public sealed record ConfirmPickResult(
    Guid TaskId,
    bool TaskCompleted,
    int PickedQuantity,
    int RemainingQuantity);

public sealed class ConfirmPick(
    IOutboundStore store,
    IMasterDataQueryContract masterData,
    IFacilityQueryContract facility)
{
    public async Task<ConfirmPickResult> Handle(ConfirmPickCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.LocationScan))
        {
            throw new PickLocationMismatchException(Guid.Empty, null);
        }

        if (string.IsNullOrWhiteSpace(command.SkuScan))
        {
            throw new PickSkuMismatchException(Guid.Empty, null);
        }

        if (command.Quantity <= 0)
        {
            throw new ArgumentException("Confirm quantity pozitif olmalıdır.", nameof(command.Quantity));
        }

        var task = await store.GetPickTaskAsync(command.TaskId, cancellationToken)
            ?? throw new PickTaskNotFoundException(command.TaskId);

        if (task.Status is PickTaskStatus.Completed or PickTaskStatus.NotFound or PickTaskStatus.Cancelled)
        {
            throw new InvalidPickTaskStateException($"Pick task {task.Status} durumundayken confirm edilemez.");
        }

        var location = await facility.GetLocationByCodeAsync(task.WarehouseId, command.LocationScan.Trim(), cancellationToken);
        if (location is null || location.Id != task.LocationId)
        {
            throw new PickLocationMismatchException(task.LocationId, location?.Code ?? command.LocationScan.Trim());
        }

        var sku = await masterData.GetSkuByBarcodeAsync(command.SkuScan.Trim(), cancellationToken);
        if (sku is null || sku.Id != task.SkuId)
        {
            throw new PickSkuMismatchException(task.SkuId, command.SkuScan.Trim());
        }

        if (task.PickedQuantity + command.Quantity > task.RequiredQuantity)
        {
            throw new PickQuantityExceededException(task.RequiredQuantity, task.PickedQuantity, command.Quantity);
        }

        await store.BeginTransactionAsync(cancellationToken);
        try
        {
            task.ConfirmPicked(command.Quantity);

            var order = await store.GetOrderAsync(task.OrderId, cancellationToken)
                ?? throw new OrderNotFoundException(task.OrderId);
            order.MarkPicking();

            await store.SaveChangesAsync(cancellationToken);

            var allTasks = await store.ListPickTasksByOrderAsync(task.OrderId, cancellationToken);
            var allCompleted = allTasks.All(t => t.Status == PickTaskStatus.Completed);
            if (allCompleted)
            {
                order.MarkPicked();
                await store.SaveChangesAsync(cancellationToken);
            }

            await store.CommitTransactionAsync(cancellationToken);

            return new ConfirmPickResult(
                task.Id,
                task.Status == PickTaskStatus.Completed,
                task.PickedQuantity,
                task.RequiredQuantity - task.PickedQuantity);
        }
        catch
        {
            await store.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }
}
