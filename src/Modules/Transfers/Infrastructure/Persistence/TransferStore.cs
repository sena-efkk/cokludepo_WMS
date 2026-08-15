using Microsoft.EntityFrameworkCore;
using Npgsql;
using Wms.Modules.Transfers.Application;
using Wms.Modules.Transfers.Domain;

namespace Wms.Modules.Transfers.Infrastructure.Persistence;

public sealed class TransferStore(TransfersDbContext db) : ITransferStore
{
    public async Task<TransferOrder?> GetTransferAsync(Guid transferId, CancellationToken cancellationToken)
    {
        return await db.TransferOrders
            .Include(t => t.Lines)
            .FirstOrDefaultAsync(t => t.Id == transferId, cancellationToken);
    }

    public async Task<TransferOrder?> GetTransferByRequestIdAsync(Guid requestId, CancellationToken cancellationToken)
    {
        return await db.TransferOrders
            .Include(t => t.Lines)
            .FirstOrDefaultAsync(t => t.RequestId == requestId, cancellationToken);
    }

    public async Task<TransferOrder?> GetTransferByNumberAsync(string transferNumber, CancellationToken cancellationToken)
    {
        return await db.TransferOrders
            .Include(t => t.Lines)
            .FirstOrDefaultAsync(t => t.TransferNumber == transferNumber.ToUpperInvariant(), cancellationToken);
    }

    public async Task<IReadOnlyList<TransferOrder>> ListTransfersAsync(
        Guid? warehouseId,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = db.TransferOrders.AsNoTracking().Include(t => t.Lines).AsQueryable();

        if (warehouseId.HasValue)
        {
            query = query.Where(t => t.SourceWarehouseId == warehouseId.Value || t.DestinationWarehouseId == warehouseId.Value);
        }

        var result = await query
            .OrderByDescending(t => t.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return result.AsReadOnly();
    }

    public async Task AddTransferAsync(TransferOrder transfer, CancellationToken cancellationToken)
    {
        await db.TransferOrders.AddAsync(transfer, cancellationToken);
    }

    public async Task<TransferReceiveRecord?> GetReceiveRecordByRequestIdAsync(Guid requestId, CancellationToken cancellationToken)
    {
        return await db.TransferReceiveRecords
            .FirstOrDefaultAsync(r => r.RequestId == requestId, cancellationToken);
    }

    public async Task AddReceiveRecordAsync(TransferReceiveRecord record, CancellationToken cancellationToken)
    {
        await db.TransferReceiveRecords.AddAsync(record, cancellationToken);
    }

    public async Task<TransferDiscrepancy?> GetDiscrepancyByRequestIdAsync(Guid requestId, CancellationToken cancellationToken)
    {
        return await db.TransferDiscrepancies
            .FirstOrDefaultAsync(d => d.RequestId == requestId, cancellationToken);
    }

    public async Task AddDiscrepancyAsync(TransferDiscrepancy discrepancy, CancellationToken cancellationToken)
    {
        await db.TransferDiscrepancies.AddAsync(discrepancy, cancellationToken);
    }

    public async Task<IReadOnlyList<TransferDiscrepancy>> ListDiscrepanciesAsync(Guid transferLineId, CancellationToken cancellationToken)
    {
        var result = await db.TransferDiscrepancies
            .AsNoTracking()
            .Where(d => d.TransferLineId == transferLineId)
            .OrderBy(d => d.CreatedAt)
            .ToListAsync(cancellationToken);
        return result.AsReadOnly();
    }

    public async Task<int> GetOpenInTransitTotalAsync(CancellationToken cancellationToken)
    {
        return await db.Database.SqlQueryRaw<int>(
                """
                SELECT COALESCE(SUM(shipped_quantity - received_quantity - confirmed_variance_quantity), 0)::int AS "Value"
                FROM transfers.transfer_line
                """)
            .FirstAsync(cancellationToken);
    }

    public async Task<int> GetOpenInTransitBySkuAsync(Guid skuId, CancellationToken cancellationToken)
    {
        return await db.Database.SqlQueryRaw<int>(
                """
                SELECT COALESCE(SUM(shipped_quantity - received_quantity - confirmed_variance_quantity), 0)::int AS "Value"
                FROM transfers.transfer_line
                WHERE sku_id = {0}
                """,
                skuId)
            .FirstAsync(cancellationToken);
    }

    public async Task<TransferOrder?> GetTransferByOutboundOrderIdAsync(Guid outboundOrderId, CancellationToken cancellationToken)
    {
        return await db.TransferOrders
            .Include(t => t.Lines)
            .FirstOrDefaultAsync(t => t.OutboundOrderId == outboundOrderId, cancellationToken);
    }

    public async Task<TransferOrder?> GetTransferByInboundReceiptIdAsync(Guid inboundReceiptId, CancellationToken cancellationToken)
    {
        return await db.TransferOrders
            .Include(t => t.Lines)
            .FirstOrDefaultAsync(t => t.InboundReceiptId == inboundReceiptId, cancellationToken);
    }

    public async Task<Wms.Integration.Inbox.InboxMessage?> GetInboxMessageAsync(string consumer, Guid eventId, CancellationToken cancellationToken)
    {
        return await db.InboxMessages
            .FirstOrDefaultAsync(m => m.Consumer == consumer && m.EventId == eventId, cancellationToken);
    }

    public async Task AddInboxMessageAsync(Wms.Integration.Inbox.InboxMessage message, CancellationToken cancellationToken)
    {
        await db.InboxMessages.AddAsync(message, cancellationToken);
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

    public async Task<TransferSaveOutcome> SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return TransferSaveOutcome.Saved;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
            return TransferSaveOutcome.DuplicateRequest;
        }
    }
}
