using System.Net;
using System.Text;
using Wms.Modules.Fulfillment.Application;
using Wms.Modules.Fulfillment.Application.Optimization;
using Xunit;

namespace Wms.FulfillmentTests.Application;

public sealed class OptimizationTests
{
    private static readonly OptimizationOptions DefaultOptions = new();

    private static readonly Guid SkuA = Guid.NewGuid();
    private static readonly Guid SkuB = Guid.NewGuid();
    private static readonly RouteQueryPoint Bursa = new(40.1885m, 29.0610m);
    private static readonly RouteQueryPoint Istanbul = new(41.0082m, 28.9784m);
    private static readonly RouteQueryPoint Inegol = new(40.0806m, 29.5097m);
    private static readonly RouteQueryPoint CustomerNearBursa = new(40.1900m, 29.0700m);

    private static SourcingCandidate CompleteSingle(Guid warehouseId, string code, (Guid Sku, int Qty, int Atp)[] lines) =>
        new(
            0,
            warehouseId,
            code,
            true,
            lines.Length,
            lines.Length,
            90,
            ["All lines available"],
            [
                new SourcingWarehouseAssignment(
                    warehouseId,
                    code,
                    lines.Select(l => new SourcingCandidateLine(l.Sku, $"SKU-{l.Sku:N}"[..4], l.Qty, l.Atp, l.Atp >= l.Qty)).ToList())
            ],
            null,
            null);

    private static SourcingCandidate PartialSingle(Guid warehouseId, string code, (Guid Sku, int Qty, int Atp)[] lines) =>
        new(
            0,
            warehouseId,
            code,
            false,
            lines.Count(l => l.Atp >= l.Qty),
            lines.Length,
            50,
            ["Partial"],
            [
                new SourcingWarehouseAssignment(
                    warehouseId,
                    code,
                    lines.Select(l => new SourcingCandidateLine(l.Sku, $"SKU-{l.Sku:N}"[..4], l.Qty, l.Atp, l.Atp >= l.Qty)).ToList())
            ],
            null,
            null);

    private static OptimizationContext Context(
        IReadOnlyList<SourcingCandidate> candidates,
        IReadOnlyList<SourcingLineInput> lines,
        IReadOnlyDictionary<Guid, RouteQueryPoint> coordinates,
        RouteQueryPoint? destination = null,
        IReadOnlyDictionary<(Guid, Guid), string>? risk = null) =>
        new(
            candidates,
            lines,
            risk ?? new Dictionary<(Guid, Guid), string>(),
            coordinates,
            destination);

    private static SourcingOptimizer Optimizer(IRouteProvider routes, OptimizationOptions? options = null)
    {
        var o = options ?? DefaultOptions;
        return new SourcingOptimizer(o, routes, new FulfillmentCostModel(o));
    }

    // 1 — Nearest strategy deterministic.
    [Fact]
    public async Task Nearest_strategy_picks_nearest_complete_warehouse()
    {
        var near = Guid.NewGuid();
        var far = Guid.NewGuid();
        var candidates = new[]
        {
            CompleteSingle(near, "NEAR", [(SkuA, 5, 10)]),
            CompleteSingle(far, "FAR", [(SkuA, 5, 10)]),
        };
        var coordinates = new Dictionary<Guid, RouteQueryPoint> { [near] = Bursa, [far] = Istanbul };
        var routes = new FakeRouteProvider(
            new Dictionary<Guid, RouteInfo> { [near] = new(5m, 10m, "FAKE"), [far] = new(50m, 60m, "FAKE") },
            coordinates);
        var context = Context(candidates, [new SourcingLineInput(SkuA, 5)], coordinates, CustomerNearBursa);

        var plan = await Optimizer(routes).OptimizeAsync(context, OptimizationStrategy.NearestAvailable, CancellationToken.None);

        Assert.NotNull(plan);
        Assert.Equal(near, plan!.Warehouses.Single().WarehouseId);
        Assert.Equal(OptimizationStatus.Optimal, plan.Status);
    }

    // 2 — Greedy strategy deterministic.
    [Fact]
    public async Task Greedy_strategy_covers_lines_deterministically()
    {
        var whA = Guid.NewGuid();
        var whB = Guid.NewGuid();
        var candidates = new[]
        {
            PartialSingle(whA, "A", [(SkuA, 5, 5), (SkuB, 5, 0)]),
            PartialSingle(whB, "B", [(SkuA, 5, 0), (SkuB, 5, 5)]),
        };
        var coordinates = new Dictionary<Guid, RouteQueryPoint> { [whA] = Bursa, [whB] = Istanbul };
        var routes = new FakeRouteProvider(
            new Dictionary<Guid, RouteInfo> { [whA] = new(5m, 10m, "FAKE"), [whB] = new(7m, 12m, "FAKE") },
            coordinates);
        var context = Context(
            candidates,
            [new SourcingLineInput(SkuA, 5), new SourcingLineInput(SkuB, 5)],
            coordinates,
            CustomerNearBursa);

        var plan = await Optimizer(routes).OptimizeAsync(context, OptimizationStrategy.GreedyCoverage, CancellationToken.None);

        Assert.NotNull(plan);
        Assert.Equal(2, plan!.Warehouses.Count);
        Assert.True(plan.CompleteCoverage);
        Assert.Equal(2, plan.ShipmentCount);
    }

    // 3 + 4 — Optimized feasible seçer; ATP yetersizse complete saymaz.
    [Fact]
    public async Task Optimized_strategy_picks_feasible_plan_only()
    {
        var sufficient = Guid.NewGuid();
        var insufficient = Guid.NewGuid();
        var candidates = new[]
        {
            CompleteSingle(sufficient, "OK", [(SkuA, 5, 10)]),
            PartialSingle(insufficient, "LOW", [(SkuA, 5, 2)]),
        };
        var coordinates = new Dictionary<Guid, RouteQueryPoint> { [sufficient] = Bursa, [insufficient] = Istanbul };
        var routes = new FakeRouteProvider(
            new Dictionary<Guid, RouteInfo> { [sufficient] = new(10m, 15m, "FAKE"), [insufficient] = new(1m, 2m, "FAKE") },
            coordinates);
        var context = Context(candidates, [new SourcingLineInput(SkuA, 5)], coordinates, CustomerNearBursa);

        var plan = await Optimizer(routes).OptimizeAsync(context, OptimizationStrategy.Optimized, CancellationToken.None);

        Assert.NotNull(plan);
        Assert.Equal(sufficient, plan!.Warehouses.Single().WarehouseId);
        Assert.True(plan.CompleteCoverage);
    }

    // 5 — Optimizer yalnız feasible set'i kullanır.
    [Fact]
    public async Task Optimizer_only_uses_feasible_candidates()
    {
        var active = Guid.NewGuid();
        var candidates = new[] { CompleteSingle(active, "ACTIVE", [(SkuA, 5, 10)]) };
        var coordinates = new Dictionary<Guid, RouteQueryPoint> { [active] = Bursa };
        var routes = new FakeRouteProvider(new Dictionary<Guid, RouteInfo> { [active] = new(10m, 15m, "FAKE") }, coordinates);
        var context = Context(candidates, [new SourcingLineInput(SkuA, 5)], coordinates, CustomerNearBursa);

        var plan = await Optimizer(routes).OptimizeAsync(context, OptimizationStrategy.Optimized, CancellationToken.None);

        Assert.Equal(active, plan!.Warehouses.Single().WarehouseId);
    }

    // 6 — Max split korunur.
    [Fact]
    public async Task Optimized_never_exceeds_max_split()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        var skuC = Guid.NewGuid();
        var candidates = new[]
        {
            PartialSingle(a, "A", [(SkuA, 5, 5), (SkuB, 5, 0), (skuC, 5, 0)]),
            PartialSingle(b, "B", [(SkuA, 5, 0), (SkuB, 5, 5), (skuC, 5, 0)]),
            PartialSingle(c, "C", [(SkuA, 5, 0), (SkuB, 5, 0), (skuC, 5, 5)]),
        };
        var coordinates = new Dictionary<Guid, RouteQueryPoint> { [a] = Bursa, [b] = Istanbul, [c] = Inegol };
        var routes = new FakeRouteProvider(
            new Dictionary<Guid, RouteInfo>
            {
                [a] = new(5m, 10m, "FAKE"),
                [b] = new(6m, 11m, "FAKE"),
                [c] = new(7m, 12m, "FAKE"),
            },
            coordinates);
        var context = Context(
            candidates,
            [new SourcingLineInput(SkuA, 5), new SourcingLineInput(SkuB, 5), new SourcingLineInput(skuC, 5)],
            coordinates,
            CustomerNearBursa);

        var plan = await Optimizer(routes).OptimizeAsync(context, OptimizationStrategy.Optimized, CancellationToken.None);

        Assert.Null(plan);
    }

    // 7 — Full single plan split'ten ucuzsa single kazanır (Scenario D).
    [Fact]
    public async Task Single_warehouse_wins_when_cheaper_than_split()
    {
        var single = Guid.NewGuid();
        var splitA = Guid.NewGuid();
        var splitB = Guid.NewGuid();
        var candidates = new[]
        {
            CompleteSingle(single, "SINGLE", [(SkuA, 5, 10), (SkuB, 5, 10)]),
            PartialSingle(splitA, "SA", [(SkuA, 5, 5), (SkuB, 5, 0)]),
            PartialSingle(splitB, "SB", [(SkuA, 5, 0), (SkuB, 5, 5)]),
        };
        var coordinates = new Dictionary<Guid, RouteQueryPoint> { [single] = Bursa, [splitA] = Istanbul, [splitB] = Inegol };
        var routes = new FakeRouteProvider(
            new Dictionary<Guid, RouteInfo>
            {
                [single] = new(30m, 40m, "FAKE"),
                [splitA] = new(120m, 130m, "FAKE"),
                [splitB] = new(125m, 135m, "FAKE"),
            },
            coordinates);
        var context = Context(
            candidates,
            [new SourcingLineInput(SkuA, 5), new SourcingLineInput(SkuB, 5)],
            coordinates,
            CustomerNearBursa);

        var plan = await Optimizer(routes).OptimizeAsync(context, OptimizationStrategy.Optimized, CancellationToken.None);

        Assert.NotNull(plan);
        Assert.Equal(single, plan!.Warehouses.Single().WarehouseId);
        Assert.Equal(1, plan.ShipmentCount);
    }

    // 8 — Split gerekliyse split plan üretilir (Scenario C).
    [Fact]
    public async Task Split_plan_wins_when_necessary()
    {
        var splitA = Guid.NewGuid();
        var splitB = Guid.NewGuid();
        var candidates = new[]
        {
            PartialSingle(splitA, "SA", [(SkuA, 5, 5), (SkuB, 5, 0)]),
            PartialSingle(splitB, "SB", [(SkuA, 5, 0), (SkuB, 5, 5)]),
        };
        var coordinates = new Dictionary<Guid, RouteQueryPoint> { [splitA] = Bursa, [splitB] = Istanbul };
        var routes = new FakeRouteProvider(
            new Dictionary<Guid, RouteInfo> { [splitA] = new(5m, 10m, "FAKE"), [splitB] = new(6m, 11m, "FAKE") },
            coordinates);
        var context = Context(
            candidates,
            [new SourcingLineInput(SkuA, 5), new SourcingLineInput(SkuB, 5)],
            coordinates,
            CustomerNearBursa);

        var plan = await Optimizer(routes).OptimizeAsync(context, OptimizationStrategy.Optimized, CancellationToken.None);

        Assert.NotNull(plan);
        Assert.Equal(2, plan!.ShipmentCount);
        Assert.True(plan.CompleteCoverage);
    }

    // 9 — Route distance total cost'a yansır.
    [Fact]
    public async Task Distance_reflects_in_transport_cost()
    {
        var wh = Guid.NewGuid();
        var candidates = new[] { CompleteSingle(wh, "W", [(SkuA, 5, 10)]) };
        var coordinates = new Dictionary<Guid, RouteQueryPoint> { [wh] = Bursa };
        var near = new FakeRouteProvider(new Dictionary<Guid, RouteInfo> { [wh] = new(10m, 15m, "FAKE") }, coordinates);
        var far = new FakeRouteProvider(new Dictionary<Guid, RouteInfo> { [wh] = new(100m, 150m, "FAKE") }, coordinates);
        var context = Context(candidates, [new SourcingLineInput(SkuA, 5)], coordinates, CustomerNearBursa);

        var nearPlan = await Optimizer(near).OptimizeAsync(context, OptimizationStrategy.Optimized, CancellationToken.None);
        var farPlan = await Optimizer(far).OptimizeAsync(context, OptimizationStrategy.Optimized, CancellationToken.None);

        Assert.True(farPlan!.Cost.TransportCost > nearPlan!.Cost.TransportCost);
        Assert.Equal(10m * DefaultOptions.CostPerKm + 15m * DefaultOptions.DriverCostPerMinute, nearPlan.Cost.TransportCost);
    }

    // 10 + 11 — Risk penalty ATP'yi değiştirmez; RED cost'u etkiler.
    [Fact]
    public async Task Risk_affects_cost_not_atp()
    {
        var red = Guid.NewGuid();
        var green = Guid.NewGuid();
        var candidates = new[]
        {
            CompleteSingle(red, "RED", [(SkuA, 5, 10)]),
            CompleteSingle(green, "GREEN", [(SkuA, 5, 10)]),
        };
        var coordinates = new Dictionary<Guid, RouteQueryPoint> { [red] = Bursa, [green] = Istanbul };
        var routes = new FakeRouteProvider(
            new Dictionary<Guid, RouteInfo> { [red] = new(10m, 15m, "FAKE"), [green] = new(10m, 15m, "FAKE") },
            coordinates);
        var optimizer = Optimizer(routes);

        var redPlan = await optimizer.OptimizeAsync(
            Context([candidates[0]], [new SourcingLineInput(SkuA, 5)], coordinates, CustomerNearBursa, new Dictionary<(Guid, Guid), string> { [(red, SkuA)] = "RED" }),
            OptimizationStrategy.Optimized,
            CancellationToken.None);
        var greenPlan = await optimizer.OptimizeAsync(
            Context([candidates[1]], [new SourcingLineInput(SkuA, 5)], coordinates, CustomerNearBursa, new Dictionary<(Guid, Guid), string> { [(green, SkuA)] = "GREEN" }),
            OptimizationStrategy.Optimized,
            CancellationToken.None);

        Assert.Equal(10, redPlan!.Warehouses.Single().Lines.Single().Atp);
        Assert.True(redPlan.Cost.InventoryReliabilityPenalty > greenPlan!.Cost.InventoryReliabilityPenalty);
        Assert.Equal(DefaultOptions.RiskPenaltyRed, redPlan.Cost.InventoryReliabilityPenalty);
    }

    // 12 — Scarcity penalty deterministic.
    [Fact]
    public async Task Scarcity_penalty_applies_when_remaining_ratio_low()
    {
        var scarce = Guid.NewGuid();
        var plenty = Guid.NewGuid();
        var candidates = new[]
        {
            CompleteSingle(scarce, "SCARCE", [(SkuA, 10, 11)]),
            CompleteSingle(plenty, "PLENTY", [(SkuA, 10, 100)]),
        };
        var coordinates = new Dictionary<Guid, RouteQueryPoint> { [scarce] = Bursa, [plenty] = Istanbul };
        var routes = new FakeRouteProvider(
            new Dictionary<Guid, RouteInfo> { [scarce] = new(10m, 15m, "FAKE"), [plenty] = new(10m, 15m, "FAKE") },
            coordinates);
        var optimizer = Optimizer(routes);

        var scarcePlan = await optimizer.OptimizeAsync(
            Context([candidates[0]], [new SourcingLineInput(SkuA, 10)], coordinates, CustomerNearBursa),
            OptimizationStrategy.Optimized,
            CancellationToken.None);
        var plentyPlan = await optimizer.OptimizeAsync(
            Context([candidates[1]], [new SourcingLineInput(SkuA, 10)], coordinates, CustomerNearBursa),
            OptimizationStrategy.Optimized,
            CancellationToken.None);

        Assert.Equal(DefaultOptions.ScarcityPenaltyCost, scarcePlan!.Cost.ScarcityPenalty);
        Assert.Equal(0m, plentyPlan!.Cost.ScarcityPenalty);
    }

    // 13 — OSRM sonucu kullanılır.
    [Fact]
    public async Task Osrm_result_is_used_when_available()
    {
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"code":"Ok","routes":[{"distance":12345.6,"duration":678.9}]}""", Encoding.UTF8, "application/json"),
        });

        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000") };
        var osrm = new OsrmRouteProvider(http);
        var route = await osrm.GetRouteAsync(Bursa, Istanbul, CancellationToken.None);

        Assert.Equal("OSRM", route.Source);
        Assert.Equal(12.3456m, route.DistanceKm);
        Assert.Equal(11.315m, route.DurationMinutes);
    }

    // 14 — OSRM failure → Haversine fallback (açık işaretleme).
    [Fact]
    public async Task Osrm_failure_falls_back_to_haversine_explicitly()
    {
        var wh = Guid.NewGuid();
        var candidates = new[] { CompleteSingle(wh, "W", [(SkuA, 5, 10)]) };
        var coordinates = new Dictionary<Guid, RouteQueryPoint> { [wh] = Bursa };
        var failingOsrm = new OsrmRouteProvider(new HttpClient(new StubHttpHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)))
        {
            BaseAddress = new Uri("http://localhost:5000"),
        });
        var optimizer = Optimizer(failingOsrm);
        var context = Context(candidates, [new SourcingLineInput(SkuA, 5)], coordinates, Istanbul);

        var plan = await optimizer.OptimizeAsync(context, OptimizationStrategy.Optimized, CancellationToken.None);

        Assert.NotNull(plan);
        Assert.Contains("HAVERSINE_FALLBACK", plan!.RouteSource);
    }

    // 15 — Solver timeout → greedy fallback (açık işaretleme).
    [Fact]
    public async Task Solver_timeout_falls_back_to_greedy()
    {
        var whA = Guid.NewGuid();
        var whB = Guid.NewGuid();
        var candidates = new[]
        {
            CompleteSingle(whA, "A", [(SkuA, 5, 10)]),
            CompleteSingle(whB, "B", [(SkuA, 5, 10)]),
        };
        var coordinates = new Dictionary<Guid, RouteQueryPoint> { [whA] = Bursa, [whB] = Istanbul };
        var slowRoutes = new SlowRouteProvider();
        var options = new OptimizationOptions { SolverTimeoutMs = 20 };
        var optimizer = Optimizer(slowRoutes, options);
        var context = Context(candidates, [new SourcingLineInput(SkuA, 5)], coordinates, CustomerNearBursa);

        var plan = await optimizer.OptimizeAsync(context, OptimizationStrategy.Optimized, CancellationToken.None);

        Assert.NotNull(plan);
        Assert.Equal(OptimizationStatus.Timeout, plan!.Status);
        Assert.Equal("GREEDY_FALLBACK", plan.StrategyUsed);
    }

    // 20 — Cost breakdown toplam ile tutarlı.
    [Fact]
    public async Task Cost_breakdown_components_sum_to_total()
    {
        var wh = Guid.NewGuid();
        var candidates = new[] { CompleteSingle(wh, "W", [(SkuA, 5, 10), (SkuB, 3, 10)]) };
        var coordinates = new Dictionary<Guid, RouteQueryPoint> { [wh] = Bursa };
        var routes = new FakeRouteProvider(new Dictionary<Guid, RouteInfo> { [wh] = new(42m, 55m, "FAKE") }, coordinates);
        var context = Context(
            candidates,
            [new SourcingLineInput(SkuA, 5), new SourcingLineInput(SkuB, 3)],
            coordinates,
            Istanbul,
            new Dictionary<(Guid, Guid), string> { [(wh, SkuA)] = "YELLOW" });

        var plan = await Optimizer(routes).OptimizeAsync(context, OptimizationStrategy.Optimized, CancellationToken.None);

        var sum = plan!.Cost.TransportCost
            + plan.Cost.DispatchCost
            + plan.Cost.PackagingCost
            + plan.Cost.HandlingCost
            + plan.Cost.PickingCost
            + plan.Cost.SplitPenalty
            + plan.Cost.InventoryReliabilityPenalty
            + plan.Cost.ScarcityPenalty
            + plan.Cost.SlaPenalty;

        Assert.Equal(sum, plan.Cost.TotalCost);
        Assert.Equal(DefaultOptions.RiskPenaltyYellow, plan.Cost.InventoryReliabilityPenalty);
    }

    // 21 — Money decimal precision korunur.
    [Fact]
    public async Task Money_calculations_use_decimal_precision()
    {
        var wh = Guid.NewGuid();
        var candidates = new[] { CompleteSingle(wh, "W", [(SkuA, 3, 10)]) };
        var coordinates = new Dictionary<Guid, RouteQueryPoint> { [wh] = Bursa };
        var routes = new FakeRouteProvider(new Dictionary<Guid, RouteInfo> { [wh] = new(10m, 15m, "FAKE") }, coordinates);
        var context = Context(candidates, [new SourcingLineInput(SkuA, 3)], coordinates, CustomerNearBursa);

        var plan = await Optimizer(routes).OptimizeAsync(context, OptimizationStrategy.Optimized, CancellationToken.None);

        Assert.Equal(5.00m, plan!.Cost.TransportCost);
        Assert.Equal(1.50m, plan.Cost.PickingCost);
    }

    // 22 — Explanation doğru.
    [Fact]
    public async Task Explanations_describe_selection()
    {
        var wh = Guid.NewGuid();
        var candidates = new[] { CompleteSingle(wh, "W", [(SkuA, 5, 10)]) };
        var coordinates = new Dictionary<Guid, RouteQueryPoint> { [wh] = Bursa };
        var routes = new FakeRouteProvider(new Dictionary<Guid, RouteInfo> { [wh] = new(42m, 55m, "FAKE") }, coordinates);
        var context = Context(candidates, [new SourcingLineInput(SkuA, 5)], coordinates, Istanbul);

        var plan = await Optimizer(routes).OptimizeAsync(context, OptimizationStrategy.Optimized, CancellationToken.None);

        Assert.Contains(plan!.Explanations, e => e.Contains("42 km"));
        Assert.Contains(plan.Explanations, e => e.Contains("Total "));
        Assert.Contains(plan.Explanations, e => e.Contains("Risk penalty"));
    }

    // 23 — Counterfactual explanation doğru (compare).
    [Fact]
    public async Task Compare_produces_counterfactuals()
    {
        var near = Guid.NewGuid();
        var far = Guid.NewGuid();
        var candidates = new[]
        {
            CompleteSingle(near, "NEAR", [(SkuA, 5, 10)]),
            CompleteSingle(far, "FAR", [(SkuA, 5, 10)]),
        };
        var coordinates = new Dictionary<Guid, RouteQueryPoint> { [near] = Bursa, [far] = Istanbul };
        var routes = new FakeRouteProvider(
            new Dictionary<Guid, RouteInfo> { [near] = new(5m, 10m, "FAKE"), [far] = new(50m, 60m, "FAKE") },
            coordinates);
        var context = Context(candidates, [new SourcingLineInput(SkuA, 5)], coordinates, CustomerNearBursa);

        var comparison = await Optimizer(routes).CompareAsync(context, CancellationToken.None);

        Assert.NotNull(comparison.Nearest);
        Assert.NotNull(comparison.Greedy);
        Assert.NotNull(comparison.Optimized);
        Assert.NotEmpty(comparison.Counterfactuals);
        Assert.Contains(comparison.Counterfactuals, c => c.Contains("higher total cost"));
    }

    // 24 — Benchmark regression fixture (eski repo karşılaştırması için deterministik baseline).
    [Fact]
    public async Task Benchmark_fixture_matches_documented_baseline()
    {
        // 3 warehouse, 2 SKU, sabit koordinatlar: müşteri Bursa yakını.
        // Baseline (elle doğrulandı): BURSA her iki SKU'yu karşılar, split yok, en düşük toplam maliyet.
        var bursa = Guid.NewGuid();
        var istanbul = Guid.NewGuid();
        var inegol = Guid.NewGuid();
        var skuPen = Guid.NewGuid();
        var skuKag = Guid.NewGuid();

        var candidates = new[]
        {
            CompleteSingle(bursa, "BURSA", [(skuPen, 2, 5), (skuKag, 2, 5)]),
            CompleteSingle(istanbul, "ISTANBUL", [(skuPen, 2, 5), (skuKag, 2, 5)]),
            CompleteSingle(inegol, "INEGOL", [(skuPen, 2, 5), (skuKag, 2, 5)]),
        };

        var coordinates = new Dictionary<Guid, RouteQueryPoint> { [bursa] = Bursa, [istanbul] = Istanbul, [inegol] = Inegol };
        var optimizer = Optimizer(new HaversineRouteProvider());
        var context = Context(
            candidates,
            [new SourcingLineInput(skuPen, 2), new SourcingLineInput(skuKag, 2)],
            coordinates,
            CustomerNearBursa);

        var plan = await optimizer.OptimizeAsync(context, OptimizationStrategy.Optimized, CancellationToken.None);

        Assert.NotNull(plan);
        Assert.Equal(bursa, plan!.Warehouses.Single().WarehouseId);
        Assert.Equal(1, plan.ShipmentCount);
        Assert.Equal(0m, plan.Cost.SplitPenalty);
        Assert.True(plan.TotalDistanceKm < 5m);
        Assert.True(plan.Cost.TotalCost < 25m);

        var comparison = await optimizer.CompareAsync(context, CancellationToken.None);
        Assert.Equal("Optimized", comparison.RecommendedStrategy);
        Assert.True(comparison.SavingsVsNearest is null or >= 0m);
    }

    // 16 — Optimizer stok mutation yüzeyi içermez.
    [Fact]
    public void Optimizer_has_no_inventory_mutation_surface()
    {
        var optimizerType = typeof(SourcingOptimizer);
        Assert.DoesNotContain(optimizerType.GetMethods(), m => m.Name.Contains("Reserve") || m.Name.Contains("Consume"));
    }

    private sealed class FakeRouteProvider(
        IReadOnlyDictionary<Guid, RouteInfo> byWarehouse,
        IReadOnlyDictionary<Guid, RouteQueryPoint> warehouseCoordinates) : IRouteProvider
    {
        public Task<RouteInfo> GetRouteAsync(RouteQueryPoint origin, RouteQueryPoint destination, CancellationToken cancellationToken)
        {
            var match = warehouseCoordinates.FirstOrDefault(kv =>
                kv.Value.Latitude == origin.Latitude && kv.Value.Longitude == origin.Longitude);

            if (match.Key != Guid.Empty && byWarehouse.TryGetValue(match.Key, out var route))
            {
                return Task.FromResult(route);
            }

            throw new InvalidOperationException($"Fake route tanımlanmamış: ({origin.Latitude}, {origin.Longitude})");
        }
    }

    private sealed class SlowRouteProvider : IRouteProvider
    {
        public async Task<RouteInfo> GetRouteAsync(RouteQueryPoint origin, RouteQueryPoint destination, CancellationToken cancellationToken)
        {
            await Task.Delay(200, cancellationToken);
            return new RouteInfo(10m, 15m, "FAKE");
        }
    }

    private sealed class StubHttpHandler(Func<string, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request.RequestUri?.ToString() ?? string.Empty));
    }
}
