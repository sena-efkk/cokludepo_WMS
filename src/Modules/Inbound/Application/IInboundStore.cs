using Wms.Modules.Inbound.Domain;

namespace Wms.Modules.Inbound.Application;

public interface IInboundStore
{
    Task<InboundReceipt?> GetReceiptAsync(Guid receiptId, CancellationToken cancellationToken);

    Task<InboundReceipt?> GetReceiptByRequestIdAsync(Guid requestId, CancellationToken cancellationToken);

    Task<InboundReceipt?> GetReceiptByNumberAsync(string receiptNumber, CancellationToken cancellationToken);

    Task<IReadOnlyList<InboundReceipt>> ListReceiptsAsync(Guid? warehouseId, int limit, CancellationToken cancellationToken);

    Task AddReceiptAsync(InboundReceipt receipt, CancellationToken cancellationToken);

    Task LockReceiptLineAsync(Guid receiptLineId, CancellationToken cancellationToken);

    Task<ReceiptLineReceiveRecord?> GetReceiveRecordByRequestIdAsync(Guid requestId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ReceiptLineReceiveRecord>> ListReceiveRecordsAsync(Guid receiptLineId, CancellationToken cancellationToken);

    Task AddReceiveRecordAsync(ReceiptLineReceiveRecord record, CancellationToken cancellationToken);

    Task<PutawayTask?> GetPutawayTaskAsync(Guid taskId, CancellationToken cancellationToken);

    Task<IReadOnlyList<PutawayTask>> ListPutawayTasksAsync(
        Guid? warehouseId,
        PutawayTaskStatus? status,
        int limit,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<PutawayTask>> ListPutawayTasksByReceiptAsync(Guid receiptId, CancellationToken cancellationToken);

    Task<(int Total, int Completed)> GetPutawayTaskCountsAsync(Guid receiptId, CancellationToken cancellationToken);

    Task AddPutawayTaskAsync(PutawayTask task, CancellationToken cancellationToken);

    Task AddOutboxMessageAsync(Wms.Integration.Outbox.OutboxMessage message, CancellationToken cancellationToken);

    Task BeginTransactionAsync(CancellationToken cancellationToken);

    Task CommitTransactionAsync(CancellationToken cancellationToken);

    Task RollbackTransactionAsync(CancellationToken cancellationToken);

    Task<InboundSaveOutcome> SaveChangesAsync(CancellationToken cancellationToken);
}

public enum InboundSaveOutcome
{
    Saved = 1,
    DuplicateRequest = 2,
}
