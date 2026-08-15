using Wms.Modules.Inbound.Application;
using Wms.Modules.Inbound.Domain;

namespace Wms.Modules.Inbound.Application;

public sealed record InboundSummary(
    int OpenReceipts,
    int PartiallyReceivedReceipts,
    int PendingPutawayTasks,
    int InProgressPutawayTasks);

public sealed class GetInboundSummary(IInboundStore store)
{
    public async Task<InboundSummary> Handle(Guid? warehouseId, CancellationToken cancellationToken)
    {
        var receipts = await store.ListReceiptsAsync(warehouseId, 10_000, cancellationToken);
        var open = receipts.Count(r => r.Status == ReceiptStatus.Open);
        var partial = receipts.Count(r => r.Status == ReceiptStatus.PartiallyReceived);

        var tasks = await store.ListPutawayTasksAsync(warehouseId, null, 10_000, cancellationToken);
        var pendingTasks = tasks.Count(t => t.Status == PutawayTaskStatus.Pending);
        var inProgressTasks = tasks.Count(t => t.Status == PutawayTaskStatus.InProgress);

        return new InboundSummary(open, partial, pendingTasks, inProgressTasks);
    }
}
