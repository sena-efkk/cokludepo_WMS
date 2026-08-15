using Wms.Modules.Fulfillment.Application;

namespace Wms.Modules.Fulfillment.Application.Optimization;

/// <summary>
/// NearestAvailable: müşteriye en yakın warehouse önce; tek warehouse complete yoksa
/// minimum toplam mesafeli split plan (bounded).
/// </summary>
public sealed class NearestAvailableStrategy : CostBasedStrategy, ISourcingStrategy
{
    public NearestAvailableStrategy(FulfillmentCostModel costModel, IRouteProvider routeProvider)
        : base(costModel, routeProvider)
    {
    }

    public OptimizationStrategy Strategy => OptimizationStrategy.NearestAvailable;

    public async Task<OptimizedPlan?> OptimizeAsync(OptimizationContext context, CancellationToken cancellationToken)
    {
        var completeSingles = context.FeasibleCandidates
            .Where(c => c.CanFulfillCompletely && c.Warehouses.Count == 1)
            .OrderBy(c => Distance(context, c.WarehouseId))
            .ThenBy(c => c.WarehouseId)
            .ToList();

        if (completeSingles.Count > 0)
        {
            var best = completeSingles[0];
            return await BuildPlanAsync(
                Strategy,
                OptimizationStatus.Optimal,
                "NEAREST_AVAILABLE",
                context,
                best.Warehouses,
                ["Nearest warehouse with complete coverage selected."],
                cancellationToken);
        }

        var split = context.FeasibleCandidates
            .Where(c => c.CanFulfillCompletely && c.Warehouses.Count > 1)
            .OrderBy(c => c.Warehouses.Sum(w => Distance(context, w.WarehouseId)))
            .ThenBy(c => c.WarehouseId)
            .FirstOrDefault();

        if (split is not null)
        {
            return await BuildPlanAsync(
                Strategy,
                OptimizationStatus.Optimal,
                "NEAREST_AVAILABLE",
                context,
                split.Warehouses,
                ["No single warehouse covers the order — nearest split plan selected."],
                cancellationToken);
        }

        return null;
    }

    private decimal Distance(OptimizationContext context, Guid warehouseId)
    {
        var coordinate = context.WarehouseCoordinates.GetValueOrDefault(warehouseId);
        if (coordinate is null || context.Destination is null)
        {
            return decimal.MaxValue;
        }

        return HaversineRouteProvider.HaversineKm(coordinate, context.Destination);
    }
}

/// <summary>
/// GreedyCoverage: en çok line'ı karşılayan warehouse'dan başlayarak coverage tamamlanana
/// kadar ekle (MaxSplitWarehouses sınırına tabi).
/// </summary>
public sealed class GreedyCoverageStrategy : CostBasedStrategy, ISourcingStrategy
{
    private readonly OptimizationOptions _options;

    public GreedyCoverageStrategy(FulfillmentCostModel costModel, IRouteProvider routeProvider, OptimizationOptions options)
        : base(costModel, routeProvider)
    {
        _options = options;
    }

    public OptimizationStrategy Strategy => OptimizationStrategy.GreedyCoverage;

    public async Task<OptimizedPlan?> OptimizeAsync(OptimizationContext context, CancellationToken cancellationToken)
    {
        var uncovered = context.Lines.Select(l => l.SkuId).ToHashSet();
        var selected = new List<SourcingWarehouseAssignment>();

        var pool = context.FeasibleCandidates
            .Where(c => c.Warehouses.Count == 1)
            .OrderByDescending(c => c.FulfillableLineCount)
            .ThenBy(c => c.WarehouseId)
            .Take(_options.MaxCandidateWarehouses)
            .ToList();

        while (uncovered.Count > 0 && selected.Count < _options.MaxSplitWarehouses)
        {
            var next = pool
                .Where(c => !selected.Any(s => s.WarehouseId == c.WarehouseId))
                .OrderByDescending(c => c.Warehouses[0].Lines.Count(l => uncovered.Contains(l.SkuId) && l.Fulfillable))
                .ThenBy(c => c.WarehouseId)
                .FirstOrDefault();

            if (next is null)
            {
                break;
            }

            selected.Add(next.Warehouses[0]);
            foreach (var line in next.Warehouses[0].Lines.Where(l => l.Fulfillable))
            {
                uncovered.Remove(line.SkuId);
            }
        }

        var completeCoverage = uncovered.Count == 0;
        return await BuildPlanAsync(
            Strategy,
            completeCoverage ? OptimizationStatus.Optimal : OptimizationStatus.GreedyFallback,
            "GREEDY_COVERAGE",
            context,
            selected,
            completeCoverage
                ? ["Greedy coverage: warehouses added until all lines covered."]
                : ["Greedy coverage could not cover all lines within split limit."],
            cancellationToken);
    }
}

/// <summary>
/// Optimized: bounded exhaustive search (max 2 warehouse kombinasyonları) — en düşük
/// total cost'lu complete plan. OR-Tools bu abstraction arkasına eklenebilir.
/// </summary>
public sealed class OptimizedStrategy : CostBasedStrategy, ISourcingStrategy
{
    private readonly OptimizationOptions _options;

    public OptimizedStrategy(FulfillmentCostModel costModel, IRouteProvider routeProvider, OptimizationOptions options)
        : base(costModel, routeProvider)
    {
        _options = options;
    }

    public OptimizationStrategy Strategy => OptimizationStrategy.Optimized;

    public async Task<OptimizedPlan?> OptimizeAsync(OptimizationContext context, CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(_options.SolverTimeoutMs);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        OptimizedPlan? best = null;
        var timedOut = false;

        try
        {
            var pool = context.FeasibleCandidates
                .Where(c => c.Warehouses.Count == 1)
                .OrderByDescending(c => c.FulfillableLineCount)
                .ThenBy(c => c.WarehouseId)
                .Take(_options.MaxCandidateWarehouses)
                .ToList();

            var combinations = new List<IReadOnlyList<SourcingWarehouseAssignment>>();
            combinations.AddRange(pool.Select(c => c.Warehouses));

            for (var i = 0; i < pool.Count; i++)
            {
                for (var j = i + 1; j < pool.Count; j++)
                {
                    combinations.Add([.. pool[i].Warehouses, .. pool[j].Warehouses]);
                }
            }

            foreach (var plan in combinations)
            {
                linked.Token.ThrowIfCancellationRequested();

                var complete = context.Lines.All(l =>
                    plan.Any(w => w.Lines.Any(wl => wl.SkuId == l.SkuId && wl.Fulfillable)));
                if (!complete)
                {
                    continue;
                }

                var candidate = await BuildPlanAsync(
                    Strategy,
                    OptimizationStatus.Optimal,
                    "OPTIMIZED",
                    context,
                    plan,
                    cancellationToken: linked.Token);

                if (best is null || candidate.Cost.TotalCost < best.Cost.TotalCost)
                {
                    best = candidate;
                }
            }
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            timedOut = true;
        }

        if (timedOut)
        {
            var greedy = new GreedyCoverageStrategy(CostModel, RouteProvider, _options);
            var fallback = await greedy.OptimizeAsync(context, cancellationToken);
            if (fallback is not null)
            {
                return fallback with
                {
                    Strategy = OptimizationStrategy.Optimized,
                    Status = OptimizationStatus.Timeout,
                    StrategyUsed = "GREEDY_FALLBACK",
                };
            }
        }

        if (best is null)
        {
            return null;
        }

        return timedOut
            ? best with { Status = OptimizationStatus.Timeout, StrategyUsed = "OPTIMIZED_TIMEOUT_BEST_FEASIBLE" }
            : best;
    }
}
