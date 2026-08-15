using Wms.Modules.Facility.Contracts;
using Wms.Modules.Inventory.Domain.Accuracy;

namespace Wms.Modules.Inventory.Application.Accuracy;

public sealed class GetLocationRiskAssessment(
    IInventoryStore store,
    IFacilityQueryContract facility,
    InventoryRiskAnalyzer analyzer)
{
    public async Task<LocationRiskAssessment> Handle(
        Guid warehouseId,
        Guid skuId,
        Guid locationId,
        CancellationToken cancellationToken)
    {
        var location = await facility.GetLocationAsync(locationId, cancellationToken)
            ?? throw new LocationValidationException($"Location bulunamadÄ±: {locationId}");
        if (location.WarehouseId != warehouseId)
        {
            throw new LocationValidationException($"Location {location.Code} verilen warehouse'a ait deÄŸil.");
        }

        return await BuildAssessmentAsync(warehouseId, skuId, locationId, location.AllowsPicking, cancellationToken);
    }

    internal async Task<LocationRiskAssessment> BuildAssessmentAsync(
        Guid warehouseId,
        Guid skuId,
        Guid locationId,
        bool allowsPicking,
        CancellationToken cancellationToken)
    {
        var activityMap = (await store.GetPhysicalActivityAsync(warehouseId, skuId, cancellationToken))
            .ToDictionary(a => a.LocationId);
        var notFoundMap = (await store.GetNotFoundStatsAsync(warehouseId, skuId, cancellationToken))
            .ToDictionary(n => n.LocationId);
        var occurrences = await store.GetNotFoundOccurrencesAsync(warehouseId, skuId, 200, cancellationToken);
        var skuEventCounts = (await store.GetWarehouseSkuEventCountsAsync(warehouseId, cancellationToken))
            .ToDictionary(c => c.SkuId, c => c.Count180d);

        var activity = activityMap.GetValueOrDefault(locationId)
            ?? new LocationPhysicalActivity(locationId, 0, 0, 0, null);
        var notFound = notFoundMap.GetValueOrDefault(locationId)
            ?? new LocationNotFoundStats(locationId, 0, 0, null);

        var verifiedAt = await store.GetLatestVerifiedCountAtAsync(warehouseId, skuId, locationId, cancellationToken);
        var consecutiveNotFound = ConsecutiveCounter.Count(occurrences, locationId, activity.LastAt, verifiedAt);

        var velocityClass = analyzer.ClassifyVelocity(skuEventCounts, skuId);

        return analyzer.Assess(
            skuId,
            warehouseId,
            locationId,
            activity,
            notFound,
            consecutiveNotFound,
            allowsPicking,
            velocityClass,
            DateTime.UtcNow);
    }
}

public sealed class ListRiskAssessments(
    IInventoryStore store,
    IFacilityQueryContract facility,
    InventoryRiskAnalyzer analyzer)
{
    public async Task<IReadOnlyList<LocationRiskAssessment>> Handle(
        Guid? warehouseId,
        Guid? skuId,
        Guid? locationId,
        RiskLevel? riskLevel,
        int limit,
        CancellationToken cancellationToken)
    {
        var warehouses = warehouseId.HasValue
            ? [await facility.GetWarehouseAsync(warehouseId.Value, cancellationToken)
                ?? throw new WarehouseValidationException($"Warehouse bulunamadÄ±: {warehouseId.Value}")]
            : await facility.GetActiveWarehousesAsync(cancellationToken);

        var results = new List<LocationRiskAssessment>();

        foreach (var warehouse in warehouses)
        {
            if (!warehouse.IsActive)
            {
                continue;
            }

            var balances = await store.ListBalancesAsync(warehouse.Id, skuId, locationId, includeEmpty: true, cancellationToken);
            var pairs = balances
                .Select(b => (b.SkuId, b.LocationId))
                .Distinct()
                .ToList();

            if (pairs.Count == 0)
            {
                continue;
            }

            var skuEventCounts = (await store.GetWarehouseSkuEventCountsAsync(warehouse.Id, cancellationToken))
                .ToDictionary(c => c.SkuId, c => c.Count180d);

            var activityBySku = (await store.GetWarehousePhysicalActivityAsync(warehouse.Id, cancellationToken))
                .GroupBy(a => a.SkuId)
                .ToDictionary(g => g.Key, g => g.ToDictionary(
                    a => a.LocationId,
                    a => new LocationPhysicalActivity(a.LocationId, a.Count30d, a.Count90d, a.Count180d, a.LastAt)));
            var notFoundStatsBySku = (await store.GetWarehouseNotFoundStatsAsync(warehouse.Id, cancellationToken))
                .GroupBy(n => n.SkuId)
                .ToDictionary(g => g.Key, g => g.ToDictionary(
                    n => n.LocationId,
                    n => new LocationNotFoundStats(n.LocationId, n.Count7d, n.Count30d, n.LastAt)));
            var occurrencesBySku = (await store.GetWarehouseNotFoundOccurrencesAsync(warehouse.Id, 200, cancellationToken))
                .GroupBy(o => o.SkuId)
                .ToDictionary(g => g.Key, g => g.Select(o => new NotFoundOccurrence(o.LocationId, o.OccurredAt)).ToList());
            var verifiedBySkuLocation = (await store.GetWarehouseLatestVerifiedCountsAsync(warehouse.Id, cancellationToken))
                .ToDictionary(v => (v.SkuId, v.LocationId), v => v.CountedAt);

            var locationCache = new Dictionary<Guid, bool>();

            foreach (var (pairSkuId, pairLocationId) in pairs)
            {
                if (!locationCache.TryGetValue(pairLocationId, out var allowsPicking))
                {
                    var locationInfo = await facility.GetLocationAsync(pairLocationId, cancellationToken);
                    if (locationInfo is null || locationInfo.WarehouseId != warehouse.Id)
                    {
                        locationCache[pairLocationId] = false;
                        continue;
                    }

                    allowsPicking = locationInfo.AllowsPicking;
                    locationCache[pairLocationId] = allowsPicking;
                }

                activityBySku.TryGetValue(pairSkuId, out var activityMap);
                var activity = activityMap?.GetValueOrDefault(pairLocationId)
                    ?? new LocationPhysicalActivity(pairLocationId, 0, 0, 0, null);

                notFoundStatsBySku.TryGetValue(pairSkuId, out var notFoundMap);
                var notFound = notFoundMap?.GetValueOrDefault(pairLocationId)
                    ?? new LocationNotFoundStats(pairLocationId, 0, 0, null);

                occurrencesBySku.TryGetValue(pairSkuId, out var occurrences);
                var verifiedAt = verifiedBySkuLocation.GetValueOrDefault((pairSkuId, pairLocationId));
                var consecutiveNotFound = ConsecutiveCounter.Count(occurrences ?? [], pairLocationId, activity.LastAt, verifiedAt);

                var assessment = analyzer.Assess(
                    pairSkuId,
                    warehouse.Id,
                    pairLocationId,
                    activity,
                    notFound,
                    consecutiveNotFound,
                    allowsPicking,
                    analyzer.ClassifyVelocity(skuEventCounts, pairSkuId),
                    DateTime.UtcNow);

                results.Add(assessment);
            }
        }

        return results
            .Where(a => riskLevel is null || a.RiskLevel == riskLevel)
            .OrderByDescending(a => a.RiskScore)
            .ThenByDescending(a => a.DaysSinceLastMovement ?? int.MaxValue)
            .ThenBy(a => a.SkuId)
            .Take(limit)
            .ToList();
    }
}

internal static class ConsecutiveCounter
{
    public static int Count(
        IReadOnlyList<NotFoundOccurrence> occurrences,
        Guid locationId,
        DateTime? lastPhysicalAt,
        DateTime? lastVerifiedAt)
    {
        var boundary = Max(lastPhysicalAt, lastVerifiedAt);
        return boundary is null
            ? occurrences.Count(o => o.LocationId == locationId)
            : occurrences.Count(o => o.LocationId == locationId && o.OccurredAt > boundary.Value);
    }

    private static DateTime? Max(DateTime? a, DateTime? b)
    {
        if (a is null)
        {
            return b;
        }

        if (b is null)
        {
            return a;
        }

        return a.Value > b.Value ? a : b;
    }
}

public sealed class GetAbcDeadSummary(
    IInventoryStore store,
    InventoryRiskAnalyzer analyzer)
{
    public async Task<AbcDeadSummary> Handle(Guid warehouseId, CancellationToken cancellationToken)
    {
        var skuEventCounts = (await store.GetWarehouseSkuEventCountsAsync(warehouseId, cancellationToken))
            .ToDictionary(c => c.SkuId, c => c.Count180d);

        var balances = await store.ListBalancesAsync(warehouseId, null, null, includeEmpty: true, cancellationToken);
        var skus = balances.Select(b => b.SkuId).Distinct().ToList();

        var activityBySku = (await store.GetWarehousePhysicalActivityAsync(warehouseId, cancellationToken))
            .GroupBy(a => a.SkuId)
            .ToDictionary(g => g.Key, g => g.ToDictionary(a => a.LocationId, a => (DateTime?)a.LastAt));

        var classA = 0;
        var classB = 0;
        var classC = 0;
        var active = 0;
        var slow = 0;
        var dead = 0;

        foreach (var skuId in skus)
        {
            switch (analyzer.ClassifyVelocity(skuEventCounts, skuId))
            {
                case VelocityClass.A:
                    classA++;
                    break;
                case VelocityClass.B:
                    classB++;
                    break;
                default:
                    classC++;
                    break;
            }

            activityBySku.TryGetValue(skuId, out var activityMap);
            var locations = balances.Where(b => b.SkuId == skuId).Select(b => b.LocationId).Distinct();
            foreach (var locationId in locations)
            {
                var lastAt = activityMap?.GetValueOrDefault(locationId);
                int? days = lastAt is null
                    ? null
                    : Math.Max(0, (int)(DateTime.UtcNow - lastAt.Value).TotalDays);

                switch (analyzer.ClassifyState(days))
                {
                    case MovementState.Active:
                        active++;
                        break;
                    case MovementState.Slow:
                        slow++;
                        break;
                    default:
                        dead++;
                        break;
                }
            }
        }

        return new AbcDeadSummary(warehouseId, classA, classB, classC, active, slow, dead);
    }
}
