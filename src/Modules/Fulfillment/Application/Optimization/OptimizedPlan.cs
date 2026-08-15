using System.Diagnostics;
using Wms.Modules.Fulfillment.Application;

namespace Wms.Modules.Fulfillment.Application.Optimization;

public enum OptimizationStrategy
{
    NearestAvailable = 1,
    GreedyCoverage = 2,
    Optimized = 3,
    Compare = 4,
}

public enum OptimizationStatus
{
    Optimal = 1,
    Timeout = 2,
    GreedyFallback = 3,
}

public sealed record OptimizedPlan(
    OptimizationStrategy Strategy,
    OptimizationStatus Status,
    string StrategyUsed,
    IReadOnlyList<SourcingWarehouseAssignment> Warehouses,
    int ShipmentCount,
    decimal TotalDistanceKm,
    decimal TotalDurationMinutes,
    CostBreakdown Cost,
    string RouteSource,
    bool CompleteCoverage,
    IReadOnlyList<string> Explanations,
    TimeSpan EvaluationTime);

public sealed record OptimizationContext(
    IReadOnlyList<SourcingCandidate> FeasibleCandidates,
    IReadOnlyList<SourcingLineInput> Lines,
    IReadOnlyDictionary<(Guid WarehouseId, Guid SkuId), string> RiskByPair,
    IReadOnlyDictionary<Guid, RouteQueryPoint> WarehouseCoordinates,
    RouteQueryPoint? Destination);

public interface ISourcingStrategy
{
    OptimizationStrategy Strategy { get; }

    Task<OptimizedPlan?> OptimizeAsync(OptimizationContext context, CancellationToken cancellationToken);
}

public abstract class CostBasedStrategy
{
    private readonly FulfillmentCostModel _costModel;
    private readonly IRouteProvider _routeProvider;

    protected CostBasedStrategy(FulfillmentCostModel costModel, IRouteProvider routeProvider)
    {
        _costModel = costModel;
        _routeProvider = routeProvider;
    }

    protected FulfillmentCostModel CostModel => _costModel;

    protected IRouteProvider RouteProvider => _routeProvider;
    protected async Task<OptimizedPlan> BuildPlanAsync(
        OptimizationStrategy strategy,
        OptimizationStatus status,
        string strategyUsed,
        OptimizationContext context,
        IReadOnlyList<SourcingWarehouseAssignment> warehouses,
        IReadOnlyList<string>? explanations = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var routes = new List<(Guid WarehouseId, RouteInfo Route)>();

        var routeSources = new List<string>();
        foreach (var warehouse in warehouses)
        {
            var route = await GetRouteWithFallbackAsync(
                context.WarehouseCoordinates.GetValueOrDefault(warehouse.WarehouseId),
                context.Destination,
                cancellationToken);
            routes.Add((warehouse.WarehouseId, route));
            routeSources.Add(route.Source);
        }

        var cost = CostModel.Evaluate(new CostInput(routes, warehouses, context.RiskByPair, context.Lines));

        var completeCoverage = context.Lines.All(l =>
            warehouses.Any(w => w.Lines.Any(wl => wl.SkuId == l.SkuId && wl.Fulfillable)));

        var finalExplanations = new List<string>();
        if (explanations is not null)
        {
            finalExplanations.AddRange(explanations);
        }

        foreach (var (warehouseId, route) in routes)
        {
            finalExplanations.Add($"Route to customer: {route.DistanceKm:0.#} km ({route.Source})");
        }

        finalExplanations.Add($"Transport {cost.TransportCost:0.00} + Dispatch {cost.DispatchCost:0.00} + Picking {cost.PickingCost:0.00} + Packaging {cost.PackagingCost:0.00} + Handling {cost.HandlingCost:0.00}");
        finalExplanations.Add($"Risk penalty {cost.InventoryReliabilityPenalty:0.00} · Split penalty {cost.SplitPenalty:0.00} · Scarcity {cost.ScarcityPenalty:0.00}");
        finalExplanations.Add($"Total {cost.TotalCost:0.00}");

        stopwatch.Stop();

        return new OptimizedPlan(
            strategy,
            status,
            strategyUsed,
            warehouses,
            warehouses.Count,
            routes.Sum(r => r.Route.DistanceKm),
            routes.Sum(r => r.Route.DurationMinutes),
            cost,
            string.Join(",", routeSources.Distinct()),
            completeCoverage,
            finalExplanations,
            stopwatch.Elapsed);
    }

    protected async Task<RouteInfo> GetRouteWithFallbackAsync(
        RouteQueryPoint? warehouseCoordinate,
        RouteQueryPoint? destination,
        CancellationToken cancellationToken)
    {
        if (warehouseCoordinate is null || destination is null)
        {
            return new RouteInfo(0m, 0m, "ROUTE_DATA_MISSING");
        }

        try
        {
            return await RouteProvider.GetRouteAsync(warehouseCoordinate, destination, cancellationToken);
        }
        catch (RouteUnavailableException)
        {
            var fallback = new HaversineRouteProvider();
            var route = await fallback.GetRouteAsync(warehouseCoordinate, destination, cancellationToken);
            return route with { Source = "HAVERSINE_FALLBACK" };
        }
    }
}
