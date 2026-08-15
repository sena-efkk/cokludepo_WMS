using Microsoft.Extensions.Options;
using Wms.Integration.Telemetry;
using Wms.Modules.Facility.Contracts;
using Wms.Modules.Fulfillment.Application.Optimization;
using Wms.Modules.Fulfillment.Domain;
using Wms.Modules.Inventory.Contracts;
using Wms.Modules.MasterData.Contracts;
using Wms.Modules.Transfers.Contracts;

namespace Wms.Modules.Fulfillment.Application;

public sealed record SourcingLineInput(Guid SkuId, int Quantity);

public sealed record EvaluateSourcingCommand(
    Guid RequestId,
    string? Destination,
    IReadOnlyList<SourcingLineInput> Lines,
    decimal? DestinationLatitude = null,
    decimal? DestinationLongitude = null,
    string? Strategy = null);

public sealed record SourcingCandidateLine(
    Guid SkuId,
    string SkuCode,
    int RequestedQuantity,
    int Atp,
    bool Fulfillable);

public sealed record SourcingWarehouseAssignment(
    Guid WarehouseId,
    string WarehouseCode,
    IReadOnlyList<SourcingCandidateLine> Lines);

public sealed record SourcingShortage(
    Guid SkuId,
    string SkuCode,
    int RequestedQuantity,
    int NetworkAtp,
    int Shortage);

public sealed record SourcingCandidate(
    int Rank,
    Guid WarehouseId,
    string WarehouseCode,
    bool CanFulfillCompletely,
    int FulfillableLineCount,
    int TotalLineCount,
    int Score,
    IReadOnlyList<string> Explanations,
    IReadOnlyList<SourcingWarehouseAssignment> Warehouses,
    string? WorstRiskLevel,
    int? RecentNotFoundCount);

public sealed record SourcingEvaluation(
    Guid SourcingRequestId,
    bool Fulfillable,
    IReadOnlyList<SourcingCandidate> Candidates,
    IReadOnlyList<SourcingShortage> Shortages,
    IReadOnlyList<SourcingIncomingStock> IncomingStock,
    OptimizedPlan? Optimization = null,
    StrategyComparison? Comparison = null,
    TimeSpan? EvaluationTime = null);

public sealed record SourcingIncomingStock(Guid SkuId, string SkuCode, int InTransitQuantity);

public sealed class EvaluateSourcing(
    IFulfillmentStore store,
    IMasterDataQueryContract masterData,
    IFacilityQueryContract facility,
    IInventoryContract inventory,
    ITransferContract transfers,
    IOptions<SourcingOptions> options,
    SourcingOptimizer optimizer)
{
    public async Task<SourcingEvaluation> Handle(EvaluateSourcingCommand command, CancellationToken cancellationToken)
    {
        var existing = await store.GetSourcingRequestByRequestIdAsync(command.RequestId, cancellationToken);
        SourcingRequest request;
        if (existing is not null)
        {
            request = existing;
        }
        else
        {
            request = SourcingRequest.Create(
                command.RequestId,
                command.Destination,
                command.Lines.Select(l => new SourcingLineSpec(l.SkuId, l.Quantity)).ToList());
            await store.AddSourcingRequestAsync(request, cancellationToken);
            _ = await store.SaveChangesAsync(cancellationToken);
        }

        var skuCodes = (await masterData.GetSkusByIdsAsync(
                command.Lines.Select(l => l.SkuId).Distinct().ToList(),
                cancellationToken))
            .ToDictionary(s => s.Id, s => s.Code);

        // Network ATP (batch, N+1 yok): sku başına warehouse rollup'ları.
        var availabilityBySku = new Dictionary<Guid, IReadOnlyList<SkuWarehouseAvailability>>();
        foreach (var line in command.Lines.DistinctBy(l => l.SkuId))
        {
            availabilityBySku[line.SkuId] = await inventory.ListSkuWarehouseAvailabilityAsync(line.SkuId, cancellationToken);
        }

        var activeWarehouses = (await facility.GetActiveWarehousesAsync(cancellationToken))
            .ToDictionary(w => w.Id, w => w.Code);

        var riskPairs = command.Lines
            .SelectMany(l => availabilityBySku[l.SkuId].Select(a => new NetworkRiskPair(a.WarehouseId, l.SkuId)))
            .Distinct()
            .ToList();
        var risks = await inventory.ListSkuWarehouseRiskBatchAsync(riskPairs, cancellationToken);
        var riskByPair = risks.ToDictionary(r => (r.WarehouseId, r.SkuId));

        // Warehouse bazında line karşılanabilirlik (yalnız aktif warehouse'lar).
        var warehouseFulfillment = new Dictionary<Guid, Dictionary<Guid, int>>();
        var candidateWarehouses = new HashSet<Guid>();
        foreach (var line in command.Lines)
        {
            foreach (var availability in availabilityBySku[line.SkuId])
            {
                if (!activeWarehouses.ContainsKey(availability.WarehouseId))
                {
                    continue;
                }

                candidateWarehouses.Add(availability.WarehouseId);
                if (!warehouseFulfillment.TryGetValue(availability.WarehouseId, out var lineAtp))
                {
                    lineAtp = [];
                    warehouseFulfillment[availability.WarehouseId] = lineAtp;
                }

                lineAtp[line.SkuId] = availability.Atp;
            }
        }

        var candidates = new List<SourcingCandidate>();

        // 1) Single-warehouse planlar.
        foreach (var warehouseId in candidateWarehouses)
        {
            var plan = BuildWarehousePlan(warehouseId, command.Lines, warehouseFulfillment[warehouseId], skuCodes, activeWarehouses);
            candidates.Add(BuildCandidate([plan], command.Lines, riskByPair, options.Value, 0));
        }

        // 2) Bounded split planlar (max 2 warehouse, deterministik aday üretimi).
        var completeSingles = candidates.Where(c => c.CanFulfillCompletely).ToList();
        if (completeSingles.Count == 0 && candidateWarehouses.Count >= 2)
        {
            var ordered = candidates
                .OrderByDescending(c => c.FulfillableLineCount)
                .ThenBy(c => c.WarehouseId)
                .Take(options.Value.MaxCandidateWarehouses)
                .ToList();

            var pairs = new List<(Guid First, Guid Second)>();
            for (var i = 0; i < ordered.Count; i++)
            {
                for (var j = i + 1; j < ordered.Count && pairs.Count < options.Value.MaxCandidateWarehouses; j++)
                {
                    pairs.Add((ordered[i].WarehouseId, ordered[j].WarehouseId));
                }
            }

            foreach (var (first, second) in pairs)
            {
                var plan = new List<SourcingWarehouseAssignment>
                {
                    BuildWarehousePlan(first, command.Lines, warehouseFulfillment[first], skuCodes, activeWarehouses),
                    BuildWarehousePlan(second, command.Lines, warehouseFulfillment[second], skuCodes, activeWarehouses),
                };

                var candidate = BuildCandidate(plan, command.Lines, riskByPair, options.Value, 1);
                if (candidate.CanFulfillCompletely)
                {
                    candidates.Add(candidate);
                }
            }
        }

        var ranked = candidates
            .OrderByDescending(c => c.Score)
            .ThenByDescending(c => c.FulfillableLineCount)
            .ThenByDescending(c => c.Warehouses.Sum(w => w.Lines.Sum(l => l.Atp)))
            .ThenBy(c => c.WarehouseId)
            .Select((c, index) => c with { Rank = index + 1 })
            .ToList();

        var fulfillable = ranked.Any(c => c.CanFulfillCompletely);

        var shortages = command.Lines
            .Select(line =>
            {
                var networkAtp = availabilityBySku[line.SkuId]
                    .Where(a => activeWarehouses.ContainsKey(a.WarehouseId))
                    .Sum(a => a.Atp);
                return new SourcingShortage(
                    line.SkuId,
                    skuCodes.GetValueOrDefault(line.SkuId, "?"),
                    line.Quantity,
                    networkAtp,
                    Math.Max(0, line.Quantity - networkAtp));
            })
            .Where(s => s.Shortage > 0)
            .ToList();

        var incoming = new List<SourcingIncomingStock>();
        foreach (var line in command.Lines.DistinctBy(l => l.SkuId))
        {
            var inTransit = await transfers.GetOpenInTransitBySkuAsync(line.SkuId, cancellationToken);
            if (inTransit > 0)
            {
                incoming.Add(new SourcingIncomingStock(line.SkuId, skuCodes.GetValueOrDefault(line.SkuId, "?"), inTransit));
            }
        }

        var evaluation = new SourcingEvaluation(request.Id, fulfillable, ranked, shortages, incoming);

        WmsMetrics.SourcingRequestsTotal.Add(1);
        WmsMetrics.SetSourcingCandidateCount(ranked.Count);

        var optimizationStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var (optimization, comparison) = await OptimizeAsync(evaluation, command, skuCodes, riskByPair, cancellationToken);
        optimizationStopwatch.Stop();

        WmsMetrics.SourcingDuration.Record(optimizationStopwatch.Elapsed.TotalSeconds);

        return new SourcingEvaluation(
            request.Id,
            fulfillable,
            ranked,
            shortages,
            incoming,
            optimization,
            comparison,
            optimizationStopwatch.Elapsed);
    }

    private async Task<(OptimizedPlan? Optimization, StrategyComparison? Comparison)> OptimizeAsync(
        SourcingEvaluation evaluation,
        EvaluateSourcingCommand command,
        IReadOnlyDictionary<Guid, string> skuCodes,
        IReadOnlyDictionary<(Guid WarehouseId, Guid SkuId), SkuWarehouseRisk> riskByPair,
        CancellationToken cancellationToken)
    {
        var strategy = ParseStrategy(command.Strategy);
        if (strategy is null)
        {
            return (null, null);
        }

        var warehouseIds = evaluation.Candidates
            .SelectMany(c => c.Warehouses.Select(w => w.WarehouseId))
            .Distinct()
            .ToList();

        var warehouseInfos = new List<WarehouseInfo>();
        foreach (var warehouseId in warehouseIds)
        {
            var info = await facility.GetWarehouseAsync(warehouseId, cancellationToken);
            if (info is not null)
            {
                warehouseInfos.Add(info);
            }
        }

        var coordinates = warehouseInfos
            .Where(w => w.Latitude is not null && w.Longitude is not null)
            .ToDictionary(w => w.Id, w => new RouteQueryPoint(w.Latitude!.Value, w.Longitude!.Value));

        var destination = command.DestinationLatitude is not null && command.DestinationLongitude is not null
            ? new RouteQueryPoint(command.DestinationLatitude.Value, command.DestinationLongitude.Value)
            : (RouteQueryPoint?)null;

        var riskStrings = riskByPair.ToDictionary(kv => kv.Key, kv => kv.Value.RiskLevel);

        var context = new OptimizationContext(
            evaluation.Candidates,
            command.Lines,
            riskStrings,
            coordinates,
            destination);

        if (strategy == OptimizationStrategy.Compare)
        {
            var comparison = await optimizer.CompareAsync(context, cancellationToken);
            return (null, comparison);
        }

        var plan = await optimizer.OptimizeAsync(context, strategy.Value, cancellationToken);
        return (plan, null);
    }

    private static OptimizationStrategy? ParseStrategy(string? strategy)
    {
        if (string.IsNullOrWhiteSpace(strategy))
        {
            return null;
        }

        return strategy.Trim().ToLowerInvariant() switch
        {
            "nearest" => OptimizationStrategy.NearestAvailable,
            "greedy" => OptimizationStrategy.GreedyCoverage,
            "optimized" => OptimizationStrategy.Optimized,
            "compare" => OptimizationStrategy.Compare,
            _ => null,
        };
    }

    private static SourcingWarehouseAssignment BuildWarehousePlan(
        Guid warehouseId,
        IReadOnlyList<SourcingLineInput> lines,
        IReadOnlyDictionary<Guid, int> lineAtp,
        IReadOnlyDictionary<Guid, string> skuCodes,
        IReadOnlyDictionary<Guid, string> activeWarehouseCodes)
    {
        var planLines = lines
            .Select(l =>
            {
                var atp = lineAtp.GetValueOrDefault(l.SkuId, 0);
                return new SourcingCandidateLine(
                    l.SkuId,
                    skuCodes.GetValueOrDefault(l.SkuId, "?"),
                    l.Quantity,
                    atp,
                    atp >= l.Quantity);
            })
            .ToList();

        return new SourcingWarehouseAssignment(
            warehouseId,
            activeWarehouseCodes.GetValueOrDefault(warehouseId, "?"),
            planLines);
    }

    private static SourcingCandidate BuildCandidate(
        IReadOnlyList<SourcingWarehouseAssignment> plan,
        IReadOnlyList<SourcingLineInput> lines,
        IReadOnlyDictionary<(Guid WarehouseId, Guid SkuId), SkuWarehouseRisk> riskByPair,
        SourcingOptions options,
        int extraWarehouseCount)
    {
        var fulfillableLineCount = lines.Count(l =>
            plan.Any(w => w.Lines.Any(wl => wl.SkuId == l.SkuId && wl.Fulfillable)));

        var canFulfillCompletely = fulfillableLineCount == lines.Count;

        var worstRisk = RiskLevelValue.Green;
        var totalNotFound = 0;
        var riskLevelText = "GREEN";
        foreach (var warehouse in plan)
        {
            foreach (var line in warehouse.Lines)
            {
                if (riskByPair.TryGetValue((warehouse.WarehouseId, line.SkuId), out var risk))
                {
                    var riskValue = RiskLevelValueParser.Of(risk.RiskLevel);
                    if (riskValue > worstRisk)
                    {
                        worstRisk = riskValue;
                        riskLevelText = risk.RiskLevel;
                    }

                    totalNotFound += risk.RecentNotFoundCount;
                }
            }
        }

        var score = options.BaseScore
            + (canFulfillCompletely ? options.CompleteFulfillmentBonus : 0)
            + (extraWarehouseCount == 0 ? options.SingleWarehouseBonus : 0)
            - (extraWarehouseCount * options.SplitPenaltyPoints)
            - RiskPenalty(worstRisk, options);

        score = Math.Clamp(score, 0, 100);

        var explanations = new List<string>();
        if (canFulfillCompletely)
        {
            explanations.Add($"All {lines.Count} order lines available");
        }
        else
        {
            explanations.Add($"{fulfillableLineCount}/{lines.Count} order lines available");
        }

        if (extraWarehouseCount == 0)
        {
            explanations.Add("Single warehouse");
        }
        else
        {
            explanations.Add($"Requires {extraWarehouseCount + 1} shipments — split penalty applied");
        }

        explanations.Add($"ATP sufficient for {fulfillableLineCount} line(s)");
        explanations.Add($"Inventory risk {riskLevelText}");
        if (totalNotFound > 0)
        {
            explanations.Add($"Inventory confidence reduced: {totalNotFound} recent PickNotFound signals");
        }

        explanations.Add("In-transit stock excluded from ATP");

        var warehouseIds = string.Join("+", plan.Select(w => w.WarehouseCode));

        return new SourcingCandidate(
            0,
            plan[0].WarehouseId,
            warehouseIds,
            canFulfillCompletely,
            fulfillableLineCount,
            lines.Count,
            score,
            explanations,
            plan,
            worstRisk > RiskLevelValue.Green ? riskLevelText : null,
            totalNotFound > 0 ? totalNotFound : null);
    }

    private static int RiskPenalty(RiskLevelValue level, SourcingOptions options) => level switch
    {
        RiskLevelValue.Green => options.RiskPenaltyGreen,
        RiskLevelValue.Yellow => options.RiskPenaltyYellow,
        RiskLevelValue.Orange => options.RiskPenaltyOrange,
        _ => options.RiskPenaltyRed,
    };

    private enum RiskLevelValue
    {
        Green = 1,
        Yellow = 2,
        Orange = 3,
        Red = 4,
    }

    private static class RiskLevelValueParser
    {
        public static RiskLevelValue Of(string level) =>
            level.ToUpperInvariant() switch
            {
                "YELLOW" => RiskLevelValue.Yellow,
                "ORANGE" => RiskLevelValue.Orange,
                "RED" => RiskLevelValue.Red,
                _ => RiskLevelValue.Green,
            };
    }
}
