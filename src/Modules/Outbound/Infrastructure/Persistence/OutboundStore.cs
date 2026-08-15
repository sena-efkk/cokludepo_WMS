using Microsoft.EntityFrameworkCore;
using Npgsql;
using Wms.Modules.Outbound.Application;
using Wms.Modules.Outbound.Domain;

namespace Wms.Modules.Outbound.Infrastructure.Persistence;

public sealed class OutboundStore(OutboundDbContext db) : IOutboundStore, Wms.Integration.Outbox.IOutboxStore
{
    public async Task<FulfillmentOrder?> GetOrderAsync(Guid orderId, CancellationToken cancellationToken)
    {
        return await db.FulfillmentOrders
            .Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);
    }

    public async Task<FulfillmentOrder?> GetOrderByRequestIdAsync(Guid requestId, CancellationToken cancellationToken)
    {
        return await db.FulfillmentOrders
            .Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.RequestId == requestId, cancellationToken);
    }

    public async Task<FulfillmentOrder?> GetOrderByNumberAsync(string orderNumber, CancellationToken cancellationToken)
    {
        return await db.FulfillmentOrders
            .Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.OrderNumber == orderNumber.ToUpperInvariant(), cancellationToken);
    }

    public async Task<IReadOnlyList<FulfillmentOrder>> ListOrdersAsync(
        Guid? warehouseId,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = db.FulfillmentOrders.AsNoTracking().Include(o => o.Lines).AsQueryable();

        if (warehouseId.HasValue)
        {
            query = query.Where(o => o.WarehouseId == warehouseId.Value);
        }

        var result = await query
            .OrderByDescending(o => o.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return result.AsReadOnly();
    }

    public async Task AddOrderAsync(FulfillmentOrder order, CancellationToken cancellationToken)
    {
        await db.FulfillmentOrders.AddAsync(order, cancellationToken);
    }

    public async Task<PickTask?> GetPickTaskAsync(Guid taskId, CancellationToken cancellationToken)
    {
        return await db.PickTasks.FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken);
    }

    public async Task<IReadOnlyList<PickTask>> ListPickTasksAsync(
        Guid? warehouseId,
        PickTaskStatus? status,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = db.PickTasks.AsNoTracking().AsQueryable();

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

    public async Task<IReadOnlyList<PickTask>> ListPickTasksByOrderAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var result = await db.PickTasks
            .Where(t => t.OrderId == orderId)
            .OrderBy(t => t.CreatedAt)
            .ToListAsync(cancellationToken);
        return result.AsReadOnly();
    }

    public async Task AddPickTaskAsync(PickTask task, CancellationToken cancellationToken)
    {
        await db.PickTasks.AddAsync(task, cancellationToken);
    }

    public async Task<Package?> GetPackageByOrderAsync(Guid orderId, CancellationToken cancellationToken)
    {
        return await db.Packages.FirstOrDefaultAsync(p => p.OrderId == orderId, cancellationToken);
    }

    public async Task<Package?> GetPackageByRequestIdAsync(Guid requestId, CancellationToken cancellationToken)
    {
        return await db.Packages.FirstOrDefaultAsync(p => p.RequestId == requestId, cancellationToken);
    }

    public async Task AddPackageAsync(Package package, CancellationToken cancellationToken)
    {
        await db.Packages.AddAsync(package, cancellationToken);
    }

    public async Task<Shipment?> GetShipmentByOrderAsync(Guid orderId, CancellationToken cancellationToken)
    {
        return await db.Shipments.FirstOrDefaultAsync(s => s.OrderId == orderId, cancellationToken);
    }

    public async Task<Shipment?> GetShipmentByRequestIdAsync(Guid requestId, CancellationToken cancellationToken)
    {
        return await db.Shipments.FirstOrDefaultAsync(s => s.RequestId == requestId, cancellationToken);
    }

    public async Task AddShipmentAsync(Shipment shipment, CancellationToken cancellationToken)
    {
        await db.Shipments.AddAsync(shipment, cancellationToken);
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

    public async Task<OutboundSaveOutcome> SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return OutboundSaveOutcome.Saved;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
            return OutboundSaveOutcome.DuplicateRequest;
        }
    }
}
