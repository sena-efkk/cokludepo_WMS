using Wms.Modules.Transfers.Domain;

namespace Wms.Modules.Transfers.Application;

public enum TransferSaveOutcome
{
    Saved = 1,
    DuplicateRequest = 2,
}

public interface ITransferStore
{
    Task<TransferOrder?> GetTransferAsync(Guid transferId, CancellationToken cancellationToken);

    Task<TransferOrder?> GetTransferByRequestIdAsync(Guid requestId, CancellationToken cancellationToken);

    Task<TransferOrder?> GetTransferByNumberAsync(string transferNumber, CancellationToken cancellationToken);

    Task<IReadOnlyList<TransferOrder>> ListTransfersAsync(Guid? warehouseId, int limit, CancellationToken cancellationToken);

    Task AddTransferAsync(TransferOrder transfer, CancellationToken cancellationToken);

    Task<TransferReceiveRecord?> GetReceiveRecordByRequestIdAsync(Guid requestId, CancellationToken cancellationToken);

    Task AddReceiveRecordAsync(TransferReceiveRecord record, CancellationToken cancellationToken);

    Task<TransferDiscrepancy?> GetDiscrepancyByRequestIdAsync(Guid requestId, CancellationToken cancellationToken);

    Task AddDiscrepancyAsync(TransferDiscrepancy discrepancy, CancellationToken cancellationToken);

    Task<IReadOnlyList<TransferDiscrepancy>> ListDiscrepanciesAsync(Guid transferLineId, CancellationToken cancellationToken);

    Task<int> GetOpenInTransitTotalAsync(CancellationToken cancellationToken);

    Task<int> GetOpenInTransitBySkuAsync(Guid skuId, CancellationToken cancellationToken);

    Task<TransferOrder?> GetTransferByOutboundOrderIdAsync(Guid outboundOrderId, CancellationToken cancellationToken);

    Task<TransferOrder?> GetTransferByInboundReceiptIdAsync(Guid inboundReceiptId, CancellationToken cancellationToken);

    Task<Wms.Integration.Inbox.InboxMessage?> GetInboxMessageAsync(string consumer, Guid eventId, CancellationToken cancellationToken);

    Task AddInboxMessageAsync(Wms.Integration.Inbox.InboxMessage message, CancellationToken cancellationToken);

    Task BeginTransactionAsync(CancellationToken cancellationToken);

    Task CommitTransactionAsync(CancellationToken cancellationToken);

    Task RollbackTransactionAsync(CancellationToken cancellationToken);

    Task<TransferSaveOutcome> SaveChangesAsync(CancellationToken cancellationToken);
}
