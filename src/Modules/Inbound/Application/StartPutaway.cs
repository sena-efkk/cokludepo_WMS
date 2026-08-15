using Wms.Modules.Inbound.Domain;

namespace Wms.Modules.Inbound.Application;

public sealed class StartPutaway(IInboundStore store)
{
    public async Task<PutawayTask> Handle(Guid taskId, CancellationToken cancellationToken)
    {
        var task = await store.GetPutawayTaskAsync(taskId, cancellationToken)
            ?? throw new PutawayTaskNotFoundException(taskId);

        if (task.Status == PutawayTaskStatus.Completed)
        {
            throw new InvalidPutawayTaskStateException("Putaway task zaten tamamlanmış.");
        }

        if (task.Status == PutawayTaskStatus.Cancelled)
        {
            throw new InvalidPutawayTaskStateException("Putaway task iptal edilmiş.");
        }

        await store.BeginTransactionAsync(cancellationToken);
        try
        {
            task.Start();

            var receipt = await store.GetReceiptAsync(task.ReceiptId, cancellationToken)
                ?? throw new ReceiptNotFoundException(task.ReceiptId);
            receipt.OnPutawayTaskStarted();

            await store.SaveChangesAsync(cancellationToken);
            await store.CommitTransactionAsync(cancellationToken);
        }
        catch
        {
            await store.RollbackTransactionAsync(cancellationToken);
            throw;
        }

        return task;
    }
}
