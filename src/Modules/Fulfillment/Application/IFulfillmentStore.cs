using Wms.Modules.Fulfillment.Domain;

namespace Wms.Modules.Fulfillment.Application;

public enum FulfillmentSaveOutcome
{
    Saved = 1,
    DuplicateRequest = 2,
}

public interface IFulfillmentStore
{
    Task<SourcingRequest?> GetSourcingRequestAsync(Guid requestId, CancellationToken cancellationToken);

    Task<SourcingRequest?> GetSourcingRequestByRequestIdAsync(Guid requestId, CancellationToken cancellationToken);

    Task AddSourcingRequestAsync(SourcingRequest request, CancellationToken cancellationToken);

    Task<SourcingDecision?> GetSourcingDecisionByRequestIdAsync(Guid requestId, CancellationToken cancellationToken);

    Task<SourcingDecision?> GetSourcingDecisionBySourcingRequestIdAsync(Guid sourcingRequestId, CancellationToken cancellationToken);

    Task<IReadOnlyList<SourcingOrderLink>> ListOrderLinksAsync(Guid decisionId, CancellationToken cancellationToken);

    Task AddSourcingDecisionAsync(SourcingDecision decision, CancellationToken cancellationToken);

    Task AddOrderLinkAsync(SourcingOrderLink link, CancellationToken cancellationToken);

    Task BeginTransactionAsync(CancellationToken cancellationToken);

    Task CommitTransactionAsync(CancellationToken cancellationToken);

    Task RollbackTransactionAsync(CancellationToken cancellationToken);

    Task<FulfillmentSaveOutcome> SaveChangesAsync(CancellationToken cancellationToken);
}
