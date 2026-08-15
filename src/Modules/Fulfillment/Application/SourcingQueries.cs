using System.Text.Json;
using Wms.Modules.Fulfillment.Domain;

namespace Wms.Modules.Fulfillment.Application;

public sealed record SourcingQueryLine(Guid Id, Guid SkuId, int Quantity);

public sealed record SourcingQuery(
    Guid Id,
    Guid RequestId,
    string Destination,
    SourcingStatus Status,
    DateTime CreatedAt,
    IReadOnlyList<SourcingQueryLine> Lines,
    SourcingDecision? Decision,
    IReadOnlyList<SourcingOrderLink> OrderLinks)
{
    public object? PlanSnapshot =>
        Decision is null || string.IsNullOrWhiteSpace(Decision.PlanSnapshot)
            ? null
            : JsonSerializer.Deserialize<object>(Decision.PlanSnapshot);
}

public sealed class GetSourcing(IFulfillmentStore store)
{
    public async Task<SourcingQuery?> Handle(Guid sourcingRequestId, CancellationToken cancellationToken)
    {
        var request = await store.GetSourcingRequestAsync(sourcingRequestId, cancellationToken);
        if (request is null)
        {
            return null;
        }

        var decision = await store.GetSourcingDecisionBySourcingRequestIdAsync(request.Id, cancellationToken);
        var links = decision is null
            ? []
            : await store.ListOrderLinksAsync(decision.Id, cancellationToken);

        return new SourcingQuery(
            request.Id,
            request.RequestId,
            request.Destination,
            request.Status,
            request.CreatedAt,
            request.Lines.Select(l => new SourcingQueryLine(l.Id, l.SkuId, l.Quantity)).ToList(),
            decision,
            links);
    }
}
