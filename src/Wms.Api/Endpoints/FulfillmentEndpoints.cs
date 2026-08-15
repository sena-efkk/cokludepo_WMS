using Wms.Api.Fulfillment;
using Wms.Modules.Fulfillment.Application;

namespace Wms.Api.Endpoints;

public static class FulfillmentEndpoints
{
    public static IEndpointRouteBuilder MapFulfillmentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/fulfillment");

        group.MapPost("/sourcing/evaluate", async (EvaluateSourcingRequest request, EvaluateSourcing useCase, CancellationToken ct) =>
            await HandleAsync(
                async () =>
                {
                    var result = await useCase.Handle(
                        new EvaluateSourcingCommand(
                            request.RequestId ?? Guid.NewGuid(),
                            request.Destination,
                            request.Lines.Select(l => new SourcingLineInput(l.SkuId, l.Quantity)).ToList(),
                            request.DestinationLatitude,
                            request.DestinationLongitude,
                            request.Strategy),
                        ct);
                    return Results.Ok(EvaluateSourcingResponse.From(result));
                },
                ct));

        group.MapPost("/sourcing/{id:guid}/commit", async (Guid id, CommitSourcingRequest request, CommitSourcingDecision useCase, CancellationToken ct) =>
            await HandleAsync(
                async () =>
                {
                    var result = await useCase.Handle(
                        new CommitSourcingCommand(
                            request.RequestId ?? Guid.NewGuid(),
                            id,
                            request.Plan.Select(w => new CommitSourcingWarehouseInput(
                                w.WarehouseId,
                                w.Lines.Select(l => new CommitSourcingLineInput(l.SkuId, l.Quantity)).ToList())).ToList(),
                            request.Optimization is null
                                ? null
                                : new OptimizationSnapshotInput(
                                    request.Optimization.StrategyUsed,
                                    Enum.Parse<Wms.Modules.Fulfillment.Application.Optimization.OptimizationStatus>(request.Optimization.Status, ignoreCase: true),
                                    request.Optimization.TotalCost,
                                    request.Optimization.TotalDistanceKm,
                                    request.Optimization.RouteSource,
                                    request.Optimization.Explanations)),
                        ct);
                    return result.Outcome == SourcingCommitOutcome.Stale
                        ? Results.Conflict(new CommitSourcingResponse(
                            result.Outcome.ToString(),
                            result.DecisionId,
                            result.OrderLinks.Select(SourcingOrderLinkResponse.From).ToList(),
                            result.StaleReason))
                        : Results.Ok(new CommitSourcingResponse(
                            result.Outcome.ToString(),
                            result.DecisionId,
                            result.OrderLinks.Select(SourcingOrderLinkResponse.From).ToList(),
                            result.StaleReason));
                },
                ct));

        group.MapGet("/sourcing/{id:guid}", async (Guid id, GetSourcing useCase, CancellationToken ct) =>
        {
            var query = await useCase.Handle(id, ct);
            return query is null ? Results.NotFound() : Results.Ok(SourcingQueryResponse.From(query));
        });

        return endpoints;
    }

    private static async Task<IResult> HandleAsync(Func<Task<IResult>> action, CancellationToken ct)
    {
        try
        {
            return await action();
        }
        catch (SourcingRequestNotFoundException exception)
        {
            return Results.NotFound(exception.Message);
        }
        catch (InvalidSourcingStateException exception)
        {
            return Results.Conflict(exception.Message);
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(exception.Message);
        }
    }
}
