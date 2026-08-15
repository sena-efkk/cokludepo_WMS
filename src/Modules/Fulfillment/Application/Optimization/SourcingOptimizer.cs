using System.Diagnostics;
using Wms.Integration.Telemetry;
using Wms.Modules.Fulfillment.Application;

namespace Wms.Modules.Fulfillment.Application.Optimization;

public sealed record OptimizationTiming(TimeSpan CandidateGenerationMs, TimeSpan RoutingMs, TimeSpan SolverMs, TimeSpan TotalMs);

public sealed record StrategyComparison(
    OptimizedPlan? Nearest,
    OptimizedPlan? Greedy,
    OptimizedPlan? Optimized,
    string RecommendedStrategy,
    decimal? SavingsVsNearest,
    IReadOnlyList<string> Counterfactuals);

public sealed class SourcingOptimizer(
    OptimizationOptions options,
    IRouteProvider routeProvider,
    FulfillmentCostModel costModel)
{
    private ISourcingStrategy ResolveStrategy(OptimizationStrategy strategy) =>
        strategy switch
        {
            OptimizationStrategy.NearestAvailable => new NearestAvailableStrategy(costModel, routeProvider),
            OptimizationStrategy.GreedyCoverage => new GreedyCoverageStrategy(costModel, routeProvider, options),
            _ => new OptimizedStrategy(costModel, routeProvider, options),
        };

    public async Task<OptimizedPlan?> OptimizeAsync(OptimizationContext context, OptimizationStrategy strategy, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var plan = await ResolveStrategy(strategy).OptimizeAsync(context, cancellationToken);
        stopwatch.Stop();

        WmsMetrics.OptimizationDuration.Record(stopwatch.Elapsed.TotalSeconds);

        if (plan is null)
        {
            return null;
        }

        if (plan.Status == OptimizationStatus.Timeout || plan.Status == OptimizationStatus.GreedyFallback)
        {
            WmsMetrics.OptimizationFallbackTotal.Add(1);
        }

        if (plan.ShipmentCount > 1)
        {
            WmsMetrics.SplitPlanTotal.Add(1);
        }

        if (plan.RouteSource.Contains("HAVERSINE_FALLBACK", StringComparison.Ordinal))
        {
            WmsMetrics.RoutingFallbackTotal.Add(1);
        }

        return plan with { EvaluationTime = stopwatch.Elapsed };
    }

    public async Task<StrategyComparison> CompareAsync(OptimizationContext context, CancellationToken cancellationToken)
    {
        var nearest = await OptimizeAsync(context, OptimizationStrategy.NearestAvailable, cancellationToken);
        var greedy = await OptimizeAsync(context, OptimizationStrategy.GreedyCoverage, cancellationToken);
        var optimized = await OptimizeAsync(context, OptimizationStrategy.Optimized, cancellationToken);

        var candidates = new[] { nearest, greedy, optimized }.Where(p => p is not null).Cast<OptimizedPlan>().ToList();
        var minCost = candidates.Min(p => p.Cost.TotalCost);
        var best = candidates.FirstOrDefault(p => p.Cost.TotalCost == minCost);

        // Eşit maliyette Optimized strateji önerilir (deterministik tie-break).
        var recommendedPlan = optimized is not null && optimized.Cost.TotalCost <= minCost
            ? optimized
            : best;

        var recommended = recommendedPlan?.Strategy ?? OptimizationStrategy.Optimized;
        var savings = nearest is not null && recommendedPlan is not null
            ? nearest.Cost.TotalCost - recommendedPlan.Cost.TotalCost
            : (decimal?)null;

        var counterfactuals = new List<string>();
        if (recommendedPlan is not null)
        {
            foreach (var other in candidates.Where(p => p.Strategy != recommendedPlan.Strategy))
            {
                var delta = other.Cost.TotalCost - recommendedPlan.Cost.TotalCost;
                counterfactuals.Add(
                    $"Why not {other.Strategy}: {delta:0.00} higher total cost ({other.Cost.TotalCost:0.00} vs {recommendedPlan.Cost.TotalCost:0.00}).");
            }
        }

        return new StrategyComparison(
            nearest,
            greedy,
            optimized,
            recommended.ToString(),
            savings,
            counterfactuals);
    }
}
