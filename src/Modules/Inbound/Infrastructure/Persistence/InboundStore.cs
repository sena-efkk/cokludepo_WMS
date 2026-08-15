using Microsoft.EntityFrameworkCore;
using Npgsql;
using Wms.Modules.Inbound.Application;
using Wms.Modules.Inbound.Domain;

namespace Wms.Modules.Inbound.Infrastructure.Persistence;

public sealed class InboundStore(InboundDbContext db) : IInboundStore, Wms.Integration.Outbox.IOutboxStore
{
    public async Task<InboundReceipt?> GetReceiptAsync(Guid receiptId, CancellationToken cancellationToken)
    {
        return await db.InboundReceipts
            .Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.Id == receiptId, cancellationToken);
    }

    public async Task<InboundReceipt?> GetReceiptByRequestIdAsync(Guid requestId, CancellationToken cancellationToken)
    {
        return await db.InboundReceipts
            .Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.RequestId == requestId, cancellationToken);
    }

    public async Task<InboundReceipt?> GetReceiptByNumberAsync(string receiptNumber, CancellationToken cancellationToken)
    {
        return await db.InboundReceipts
            .Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.ReceiptNumber == receiptNumber.ToUpperInvariant(), cancellationToken);
    }

    public async Task<IReadOnlyList<InboundReceipt>> ListReceiptsAsync(
        Guid? warehouseId,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = db.InboundReceipts.AsNoTracking().Include(r => r.Lines).AsQueryable();

        if (warehouseId.HasValue)
        {
            query = query.Where(r => r.WarehouseId == warehouseId.Value);
        }

        var result = await query
            .OrderByDescending(r => r.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return result.AsReadOnly();
    }

    public async Task AddReceiptAsync(InboundReceipt receipt, CancellationToken cancellationToken)
    {
        await db.InboundReceipts.AddAsync(receipt, cancellationToken);
    }

    public async Task LockReceiptLineAsync(Guid receiptLineId, CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlRawAsync(
            "SELECT id FROM inbound.inbound_receipt_line WHERE id = {0} FOR UPDATE",
            receiptLineId);
        db.ChangeTracker.Clear();
    }

    public async Task<ReceiptLineReceiveRecord?> GetReceiveRecordByRequestIdAsync(Guid requestId, CancellationToken cancellationToken)
    {
        return await db.ReceiptLineReceiveRecords
            .FirstOrDefaultAsync(r => r.RequestId == requestId, cancellationToken);
    }

    public async Task<IReadOnlyList<ReceiptLineReceiveRecord>> ListReceiveRecordsAsync(Guid receiptLineId, CancellationToken cancellationToken)
    {
        var result = await db.ReceiptLineReceiveRecords
            .AsNoTracking()
            .Where(r => r.ReceiptLineId == receiptLineId)
            .OrderBy(r => r.ReceivedAt)
            .ToListAsync(cancellationToken);
        return result.AsReadOnly();
    }

    public async Task AddReceiveRecordAsync(ReceiptLineReceiveRecord record, CancellationToken cancellationToken)
    {
        await db.ReceiptLineReceiveRecords.AddAsync(record, cancellationToken);
    }

    public async Task<PutawayTask?> GetPutawayTaskAsync(Guid taskId, CancellationToken cancellationToken)
    {
        return await db.PutawayTasks.FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken);
    }

    public async Task<IReadOnlyList<PutawayTask>> ListPutawayTasksAsync(
        Guid? warehouseId,
        PutawayTaskStatus? status,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = db.PutawayTasks.AsNoTracking().AsQueryable();

        if (warehouseId.HasValue)
        {
            query = query.Where(t => t.WarehouseId == warehouseId.Value);
        }

        if (status.HasValue)
        {
            query = query.Where(t => t.Status == status.Value);
        }

        var result = await query
            .OrderBy(t => t.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return result.AsReadOnly();
    }

    public async Task<IReadOnlyList<PutawayTask>> ListPutawayTasksByReceiptAsync(Guid receiptId, CancellationToken cancellationToken)
    {
        var result = await db.PutawayTasks
            .AsNoTracking()
            .Where(t => t.ReceiptId == receiptId)
            .OrderBy(t => t.CreatedAt)
            .ToListAsync(cancellationToken);
        return result.AsReadOnly();
    }

    public async Task<(int Total, int Completed)> GetPutawayTaskCountsAsync(Guid receiptId, CancellationToken cancellationToken)
    {
        var total = await db.PutawayTasks.CountAsync(t => t.ReceiptId == receiptId, cancellationToken);
        var completed = await db.PutawayTasks.CountAsync(
            t => t.ReceiptId == receiptId && t.Status == PutawayTaskStatus.Completed,
            cancellationToken);
        return (total, completed);
    }

    public async Task AddPutawayTaskAsync(PutawayTask task, CancellationToken cancellationToken)
    {
        await db.PutawayTasks.AddAsync(task, cancellationToken);
    }

    public async Task AddOutboxMessageAsync(Wms.Integration.Outbox.OutboxMessage message, CancellationToken cancellationToken)
    {
        await db.OutboxMessages.AddAsync(message, cancellationToken);
    }

    public async Task<IReadOnlyList<Wms.Integration.Outbox.OutboxMessage>> FetchPendingAsync(int limit, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var result = await db.OutboxMessages
            .Where(m => m.PublishedAt == null && (m.NextAttemptAt == null || m.NextAttemptAt <= now))
            .OrderBy(m => m.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return result.AsReadOnly();
    }

    public async Task MarkPublishedAsync(Guid outboxId, DateTime at, CancellationToken cancellationToken)
    {
        await db.OutboxMessages
            .Where(m => m.Id == outboxId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(m => m.PublishedAt, at)
                    .SetProperty(m => m.AttemptCount, m => m.AttemptCount + 1)
                    .SetProperty(m => m.LastError, (string?)null)
                    .SetProperty(m => m.NextAttemptAt, (DateTime?)null),
                cancellationToken);
    }

    public async Task MarkFailedAsync(Guid outboxId, string error, DateTime nextAttemptAt, CancellationToken cancellationToken)
    {
        await db.OutboxMessages
            .Where(m => m.Id == outboxId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(m => m.AttemptCount, m => m.AttemptCount + 1)
                    .SetProperty(m => m.LastError, error)
                    .SetProperty(m => m.NextAttemptAt, nextAttemptAt),
                cancellationToken);
    }

    public async Task<int> CountPendingAsync(CancellationToken cancellationToken)
    {
        return await db.OutboxMessages.CountAsync(m => m.PublishedAt == null, cancellationToken);
    }

    public async Task<DateTime?> GetOldestPendingCreatedAtAsync(CancellationToken cancellationToken)
    {
        return await db.OutboxMessages
            .Where(m => m.PublishedAt == null)
            .OrderBy(m => m.CreatedAt)
            .Select(m => (DateTime?)m.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<int> DeletePublishedOlderThanAsync(DateTime before, CancellationToken cancellationToken)
    {
        return await db.OutboxMessages
            .Where(m => m.PublishedAt != null && m.PublishedAt < before)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? _transaction;

    public async Task BeginTransactionAsync(CancellationToken cancellationToken)
    {
        _transaction = await db.Database.BeginTransactionAsync(cancellationToken);
    }

    public async Task CommitTransactionAsync(CancellationToken cancellationToken)
    {
        if (_transaction is null)
        {
            return;
        }

        await _transaction.CommitAsync(cancellationToken);
        await _transaction.DisposeAsync();
        _transaction = null;
    }

    public async Task RollbackTransactionAsync(CancellationToken cancellationToken)
    {
        if (_transaction is null)
        {
            return;
        }

        await _transaction.RollbackAsync(cancellationToken);
        await _transaction.DisposeAsync();
        _transaction = null;
        db.ChangeTracker.Clear();
    }

    public async Task<InboundSaveOutcome> SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return InboundSaveOutcome.Saved;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
            return InboundSaveOutcome.DuplicateRequest;
        }
    }
}
