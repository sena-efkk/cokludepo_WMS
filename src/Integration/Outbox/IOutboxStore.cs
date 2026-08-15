using Wms.Integration.Outbox;

namespace Wms.Integration.Outbox;

public interface IOutboxStore
{
    Task<IReadOnlyList<OutboxMessage>> FetchPendingAsync(int limit, CancellationToken cancellationToken);

    Task MarkPublishedAsync(Guid outboxId, DateTime at, CancellationToken cancellationToken);

    Task MarkFailedAsync(Guid outboxId, string error, DateTime nextAttemptAt, CancellationToken cancellationToken);

    Task<int> CountPendingAsync(CancellationToken cancellationToken);

    Task<DateTime?> GetOldestPendingCreatedAtAsync(CancellationToken cancellationToken);

    Task<int> DeletePublishedOlderThanAsync(DateTime before, CancellationToken cancellationToken);
}
