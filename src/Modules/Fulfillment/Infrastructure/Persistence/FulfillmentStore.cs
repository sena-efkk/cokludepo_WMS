using Microsoft.EntityFrameworkCore;
using Npgsql;
using Wms.Modules.Fulfillment.Application;
using Wms.Modules.Fulfillment.Domain;

namespace Wms.Modules.Fulfillment.Infrastructure.Persistence;

public sealed class FulfillmentStore(FulfillmentDbContext db) : IFulfillmentStore
{
    public async Task<SourcingRequest?> GetSourcingRequestAsync(Guid requestId, CancellationToken cancellationToken)
    {
        return await db.SourcingRequests
            .Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.Id == requestId, cancellationToken);
    }

    public async Task<SourcingRequest?> GetSourcingRequestByRequestIdAsync(Guid requestId, CancellationToken cancellationToken)
    {
        return await db.SourcingRequests
            .Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.RequestId == requestId, cancellationToken);
    }

    public async Task AddSourcingRequestAsync(SourcingRequest request, CancellationToken cancellationToken)
    {
        await db.SourcingRequests.AddAsync(request, cancellationToken);
    }

    public async Task<SourcingDecision?> GetSourcingDecisionByRequestIdAsync(Guid requestId, CancellationToken cancellationToken)
    {
        return await db.SourcingDecisions
            .FirstOrDefaultAsync(d => d.RequestId == requestId, cancellationToken);
    }

    public async Task<SourcingDecision?> GetSourcingDecisionBySourcingRequestIdAsync(Guid sourcingRequestId, CancellationToken cancellationToken)
    {
        return await db.SourcingDecisions
            .FirstOrDefaultAsync(d => d.SourcingRequestId == sourcingRequestId, cancellationToken);
    }

    public async Task<IReadOnlyList<SourcingOrderLink>> ListOrderLinksAsync(Guid decisionId, CancellationToken cancellationToken)
    {
        var result = await db.SourcingOrderLinks
            .AsNoTracking()
            .Where(l => l.DecisionId == decisionId)
            .OrderBy(l => l.WarehouseId)
            .ToListAsync(cancellationToken);
        return result.AsReadOnly();
    }

    public async Task AddSourcingDecisionAsync(SourcingDecision decision, CancellationToken cancellationToken)
    {
        await db.SourcingDecisions.AddAsync(decision, cancellationToken);
    }

    public async Task AddOrderLinkAsync(SourcingOrderLink link, CancellationToken cancellationToken)
    {
        await db.SourcingOrderLinks.AddAsync(link, cancellationToken);
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

    public async Task<FulfillmentSaveOutcome> SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return FulfillmentSaveOutcome.Saved;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
            return FulfillmentSaveOutcome.DuplicateRequest;
        }
    }
}
