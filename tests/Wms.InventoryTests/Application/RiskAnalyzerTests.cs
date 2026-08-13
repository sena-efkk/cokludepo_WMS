using Wms.Modules.Inventory.Application.Accuracy;
using Wms.Modules.Inventory.Domain.Accuracy;
using Xunit;

namespace Wms.InventoryTests.Application;

public sealed class RiskAnalyzerTests
{
    private static readonly Guid SkuId = Guid.NewGuid();
    private static readonly Guid WarehouseId = Guid.NewGuid();
    private static readonly Guid LocationId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 8, 13, 12, 0, 0, DateTimeKind.Utc);

    private static InventoryRiskAnalyzer NewAnalyzer() => new(new RiskPolicyOptions());

    private static LocationPhysicalActivity Activity(DateTime? lastAt, int c30 = 0, int c90 = 0, int c180 = 0) =>
        new(LocationId, c30, c90, c180, lastAt);

    private static LocationNotFoundStats NotFound(int c7 = 0, int c30 = 0, DateTime? lastAt = null) =>
        new(LocationId, c7, c30, lastAt);

    // 1 — Recent activity → ACTIVE.
    [Fact]
    public void Recent_activity_yields_active_state()
    {
        var analyzer = NewAnalyzer();

        var result = analyzer.Assess(
            SkuId, WarehouseId, LocationId,
            Activity(Now.AddDays(-5), c30: 3),
            NotFound(),
            consecutiveNotFound: 0,
            allowsPicking: false,
            VelocityClass.A,
            Now);

        Assert.Equal(MovementState.Active, result.MovementState);
        Assert.DoesNotContain(result.Reasons, r => r.Code == "LONG_INACTIVITY");
        Assert.DoesNotContain(result.Reasons, r => r.Code == "DEAD_STOCK");
    }

    // 2 — 180+ days → DEAD.
    [Fact]
    public void Long_inactivity_yields_dead_state()
    {
        var analyzer = NewAnalyzer();

        var result = analyzer.Assess(
            SkuId, WarehouseId, LocationId,
            Activity(Now.AddDays(-247)),
            NotFound(),
            consecutiveNotFound: 0,
            allowsPicking: false,
            VelocityClass.C,
            Now);

        Assert.Equal(MovementState.Dead, result.MovementState);
        Assert.Equal(247, result.DaysSinceLastMovement);
        Assert.Contains(result.Reasons, r => r.Code == "LONG_INACTIVITY");
        Assert.Contains(result.Reasons, r => r.Code == "DEAD_STOCK");
    }

    // 3 — ABC classification deterministic.
    [Fact]
    public void Abc_classification_is_deterministic()
    {
        var analyzer = NewAnalyzer();
        var sku1 = Guid.NewGuid();
        var sku2 = Guid.NewGuid();
        var sku3 = Guid.NewGuid();
        var sku4 = Guid.NewGuid();
        var counts = new Dictionary<Guid, int> { [sku1] = 100, [sku2] = 50, [sku3] = 10 };

        var first = analyzer.ClassifyVelocity(counts, sku1);
        var second = analyzer.ClassifyVelocity(counts, sku1);

        Assert.Equal(first, second);
        Assert.Equal(VelocityClass.A, analyzer.ClassifyVelocity(counts, sku1));
        Assert.Equal(VelocityClass.B, analyzer.ClassifyVelocity(counts, sku2));
        Assert.Equal(VelocityClass.C, analyzer.ClassifyVelocity(counts, sku3));
        Assert.Equal(VelocityClass.C, analyzer.ClassifyVelocity(counts, sku4));
    }

    // 6 — 1 recent NotFound increases risk.
    [Fact]
    public void Single_recent_not_found_adds_points()
    {
        var analyzer = NewAnalyzer();

        var result = analyzer.Assess(
            SkuId, WarehouseId, LocationId,
            Activity(Now.AddDays(-5), c30: 3),
            NotFound(c30: 1, lastAt: Now.AddDays(-2)),
            consecutiveNotFound: 0,
            allowsPicking: false,
            VelocityClass.A,
            Now);

        Assert.Contains(result.Reasons, r => r.Code == "RECENT_NOT_FOUND" && r.Points == 20);
    }

    // 7 — 2 consecutive NotFound produces higher risk than 1.
    [Fact]
    public void Repeated_not_found_produces_higher_risk_than_single()
    {
        var analyzer = NewAnalyzer();

        var single = analyzer.Assess(
            SkuId, WarehouseId, LocationId,
            Activity(Now.AddDays(-5)),
            NotFound(c30: 1, lastAt: Now.AddDays(-1)),
            consecutiveNotFound: 1,
            allowsPicking: false,
            VelocityClass.A,
            Now);

        var repeated = analyzer.Assess(
            SkuId, WarehouseId, LocationId,
            Activity(Now.AddDays(-5)),
            NotFound(c30: 2, lastAt: Now.AddDays(-1)),
            consecutiveNotFound: 2,
            allowsPicking: false,
            VelocityClass.A,
            Now);

        Assert.Contains(repeated.Reasons, r => r.Code == "REPEATED_NOT_FOUND" && r.Points == 45);
        Assert.True(repeated.RiskScore > single.RiskScore);
        Assert.DoesNotContain(repeated.Reasons, r => r.Code == "RECENT_NOT_FOUND");
    }

    // 9 — Picking location affects score.
    [Fact]
    public void Picking_location_adds_context_points()
    {
        var analyzer = NewAnalyzer();

        var result = analyzer.Assess(
            SkuId, WarehouseId, LocationId,
            Activity(Now.AddDays(-5)),
            NotFound(),
            consecutiveNotFound: 0,
            allowsPicking: true,
            VelocityClass.A,
            Now);

        Assert.Contains(result.Reasons, r => r.Code == "PICKING_LOCATION" && r.Points == 10);
    }

    // 10 — Reasons are explained with codes, points and descriptions.
    [Fact]
    public void Reasons_are_fully_explained()
    {
        var analyzer = NewAnalyzer();

        var result = analyzer.Assess(
            SkuId, WarehouseId, LocationId,
            Activity(Now.AddDays(-400)),
            NotFound(c30: 3, lastAt: Now.AddDays(-1)),
            consecutiveNotFound: 3,
            allowsPicking: true,
            VelocityClass.C,
            Now);

        Assert.All(result.Reasons, r =>
        {
            Assert.False(string.IsNullOrWhiteSpace(r.Code));
            Assert.True(r.Points > 0);
            Assert.False(string.IsNullOrWhiteSpace(r.Description));
        });
        Assert.Equal(Math.Min(100, result.Reasons.Sum(r => r.Points)), result.RiskScore);
        Assert.Equal(RiskLevel.Red, result.RiskLevel);
    }

    // 14 — Configuration thresholds change results deterministically.
    [Fact]
    public void Policy_thresholds_change_results_deterministically()
    {
        var defaultAnalyzer = new InventoryRiskAnalyzer(new RiskPolicyOptions());
        var aggressiveAnalyzer = new InventoryRiskAnalyzer(new RiskPolicyOptions { SlowAfterDays = 10, DeadAfterDays = 60 });
        var activity = Activity(Now.AddDays(-20));

        var defaultResult = defaultAnalyzer.Assess(SkuId, WarehouseId, LocationId, activity, NotFound(), 0, false, VelocityClass.A, Now);
        var aggressiveResult = aggressiveAnalyzer.Assess(SkuId, WarehouseId, LocationId, activity, NotFound(), 0, false, VelocityClass.A, Now);

        Assert.Equal(MovementState.Active, defaultResult.MovementState);
        Assert.Equal(MovementState.Slow, aggressiveResult.MovementState);
    }

    // Red risk does not automatically correct stock — analyzer is pure, no balance mutation possible.
    [Fact]
    public void Analyzer_has_no_inventory_mutation_capability()
    {
        var analyzerType = typeof(InventoryRiskAnalyzer);
        var mutatingMethods = analyzerType.GetMethods()
            .Where(m => m.Name.Contains("Balance", StringComparison.OrdinalIgnoreCase)
                        || m.Name.Contains("Quantity", StringComparison.OrdinalIgnoreCase)
                        || m.Name.Contains("Stock", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Empty(mutatingMethods);
    }
}
