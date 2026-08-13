using Wms.Modules.Inventory.Domain.Accuracy;

namespace Wms.Modules.Inventory.Application.Accuracy;

public sealed record LocationPhysicalActivity(Guid LocationId, int Count30d, int Count90d, int Count180d, DateTime? LastAt);

public sealed record LocationNotFoundStats(Guid LocationId, int Count7d, int Count30d, DateTime? LastAt);

public sealed record NotFoundOccurrence(Guid LocationId, DateTime OccurredAt);

public sealed record SkuEventCount(Guid SkuId, int Count180d);

public sealed record RiskReason(string Code, int Points, string Description);

public sealed record LocationRiskAssessment(
    Guid SkuId,
    Guid WarehouseId,
    Guid LocationId,
    int MovementCount30d,
    int MovementCount90d,
    int MovementCount180d,
    DateTime? LastMovementAt,
    int? DaysSinceLastMovement,
    VelocityClass VelocityClass,
    MovementState MovementState,
    int NotFoundCount7d,
    int NotFoundCount30d,
    int ConsecutiveNotFound,
    DateTime? LastNotFoundAt,
    int RiskScore,
    RiskLevel RiskLevel,
    IReadOnlyList<RiskReason> Reasons);

public sealed record AbcDeadSummary(
    Guid WarehouseId,
    int ClassA,
    int ClassB,
    int ClassC,
    int StateActive,
    int StateSlow,
    int StateDead);

public sealed class InventoryRiskAnalyzer(RiskPolicyOptions policy)
{
    public RiskPolicyOptions Options => policy;

    public VelocityClass ClassifyVelocity(IReadOnlyDictionary<Guid, int> skuEventCounts180d, Guid skuId)
    {
        if (!skuEventCounts180d.TryGetValue(skuId, out var count) || count <= 0)
        {
            return VelocityClass.C;
        }

        var ranked = skuEventCounts180d
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key)
            .Select(kv => kv.Key)
            .ToList();

        var aCutoff = Math.Max(1, (int)Math.Ceiling(ranked.Count * policy.AbcARatio));
        var bCutoff = Math.Max(aCutoff, (int)Math.Ceiling(ranked.Count * policy.AbcBRatio));

        var index = ranked.IndexOf(skuId);
        if (index < aCutoff)
        {
            return VelocityClass.A;
        }

        if (index < bCutoff)
        {
            return VelocityClass.B;
        }

        return VelocityClass.C;
    }

    public MovementState ClassifyState(int? daysSinceLastMovement)
    {
        return daysSinceLastMovement is null
            ? MovementState.Dead
            : daysSinceLastMovement <= policy.SlowAfterDays
                ? MovementState.Active
                : daysSinceLastMovement <= policy.DeadAfterDays
                    ? MovementState.Slow
                    : MovementState.Dead;
    }

    public LocationRiskAssessment Assess(
        Guid skuId,
        Guid warehouseId,
        Guid locationId,
        LocationPhysicalActivity activity,
        LocationNotFoundStats notFoundStats,
        int consecutiveNotFound,
        bool allowsPicking,
        VelocityClass velocityClass,
        DateTime now)
    {
        var reasons = new List<RiskReason>();

        int? days = activity.LastAt is null
            ? null
            : Math.Max(0, (int)(now - activity.LastAt.Value).TotalDays);

        if (days is null)
        {
            reasons.Add(new RiskReason("LONG_INACTIVITY", policy.LongInactivity360Points, "Hiç fiziksel hareket kaydı yok."));
        }
        else if (days >= 360)
        {
            reasons.Add(new RiskReason("LONG_INACTIVITY", policy.LongInactivity360Points, $"{days} gündür fiziksel hareket yok (360+ gün)."));
        }
        else if (days >= 180)
        {
            reasons.Add(new RiskReason("LONG_INACTIVITY", policy.LongInactivity180Points, $"{days} gündür fiziksel hareket yok (180+ gün)."));
        }
        else if (days >= 90)
        {
            reasons.Add(new RiskReason("LONG_INACTIVITY", policy.LongInactivity90Points, $"{days} gündür fiziksel hareket yok (90+ gün)."));
        }

        if (consecutiveNotFound >= policy.RepeatedNotFoundThreshold)
        {
            reasons.Add(new RiskReason("REPEATED_NOT_FOUND", policy.RepeatedNotFoundPoints, $"{consecutiveNotFound} ardışık pick-not-found sinyali."));
        }
        else if (notFoundStats.Count30d >= 1)
        {
            reasons.Add(new RiskReason("RECENT_NOT_FOUND", policy.RecentNotFoundPoints, $"Son 30 günde {notFoundStats.Count30d} pick-not-found sinyali."));
        }

        if (allowsPicking)
        {
            reasons.Add(new RiskReason("PICKING_LOCATION", policy.PickingLocationPoints, "Picking lokasyonu — hareketsizlik daha şüphelidir."));
        }

        if (velocityClass == VelocityClass.C)
        {
            reasons.Add(new RiskReason("LOW_VELOCITY", policy.LowVelocityPoints, "Düşük hareket hacmi (Velocity Class C)."));
        }

        var state = ClassifyState(days);
        if (state == MovementState.Dead)
        {
            reasons.Add(new RiskReason("DEAD_STOCK", policy.DeadStockPoints, "Dead stock durumu."));
        }

        var score = Math.Min(100, reasons.Sum(r => r.Points));
        var level = score <= policy.GreenThreshold
            ? RiskLevel.Green
            : score <= policy.YellowThreshold
                ? RiskLevel.Yellow
                : score <= policy.OrangeThreshold
                    ? RiskLevel.Orange
                    : RiskLevel.Red;

        return new LocationRiskAssessment(
            skuId,
            warehouseId,
            locationId,
            activity.Count30d,
            activity.Count90d,
            activity.Count180d,
            activity.LastAt,
            days,
            velocityClass,
            state,
            notFoundStats.Count7d,
            notFoundStats.Count30d,
            consecutiveNotFound,
            notFoundStats.LastAt,
            score,
            level,
            reasons);
    }
}
