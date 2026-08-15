using Wms.Modules.Fulfillment.Application;

namespace Wms.Api.Fulfillment;

public sealed record SourcingLineRequest(Guid SkuId, int Quantity);

public sealed record EvaluateSourcingRequest(
    Guid? RequestId,
    string? Destination,
    IReadOnlyList<SourcingLineRequest> Lines,
    decimal? DestinationLatitude = null,
    decimal? DestinationLongitude = null,
    string? Strategy = null);

public sealed record SourcingCandidateLineResponse(
    Guid SkuId,
    string SkuCode,
    int RequestedQuantity,
    int Atp,
    bool Fulfillable)
{
    public static SourcingCandidateLineResponse From(SourcingCandidateLine line) =>
        new(line.SkuId, line.SkuCode, line.RequestedQuantity, line.Atp, line.Fulfillable);
}

public sealed record SourcingWarehouseAssignmentResponse(
    Guid WarehouseId,
    string WarehouseCode,
    IReadOnlyList<SourcingCandidateLineResponse> Lines)
{
    public static SourcingWarehouseAssignmentResponse From(SourcingWarehouseAssignment assignment) =>
        new(assignment.WarehouseId, assignment.WarehouseCode, assignment.Lines.Select(SourcingCandidateLineResponse.From).ToList());
}

public sealed record SourcingShortageResponse(
    Guid SkuId,
    string SkuCode,
    int RequestedQuantity,
    int NetworkAtp,
    int Shortage)
{
    public static SourcingShortageResponse From(SourcingShortage shortage) =>
        new(shortage.SkuId, shortage.SkuCode, shortage.RequestedQuantity, shortage.NetworkAtp, shortage.Shortage);
}

public sealed record SourcingIncomingStockResponse(Guid SkuId, string SkuCode, int InTransitQuantity)
{
    public static SourcingIncomingStockResponse From(SourcingIncomingStock incoming) =>
        new(incoming.SkuId, incoming.SkuCode, incoming.InTransitQuantity);
}

public sealed record SourcingCandidateResponse(
    int Rank,
    Guid WarehouseId,
    string WarehouseCode,
    bool CanFulfillCompletely,
    int FulfillableLineCount,
    int TotalLineCount,
    int Score,
    IReadOnlyList<string> Explanations,
    IReadOnlyList<SourcingWarehouseAssignmentResponse> Warehouses,
    string? WorstRiskLevel,
    int? RecentNotFoundCount)
{
    public static SourcingCandidateResponse From(SourcingCandidate candidate) =>
        new(
            candidate.Rank,
            candidate.WarehouseId,
            candidate.WarehouseCode,
            candidate.CanFulfillCompletely,
            candidate.FulfillableLineCount,
            candidate.TotalLineCount,
            candidate.Score,
            candidate.Explanations,
            candidate.Warehouses.Select(SourcingWarehouseAssignmentResponse.From).ToList(),
            candidate.WorstRiskLevel,
            candidate.RecentNotFoundCount);
}

public sealed record CostBreakdownResponse(
    decimal TransportCost,
    decimal DispatchCost,
    decimal PackagingCost,
    decimal HandlingCost,
    decimal PickingCost,
    decimal SplitPenalty,
    decimal InventoryReliabilityPenalty,
    decimal ScarcityPenalty,
    decimal SlaPenalty,
    decimal TotalCost)
{
    public static CostBreakdownResponse From(Wms.Modules.Fulfillment.Application.Optimization.CostBreakdown cost) =>
        new(
            cost.TransportCost,
            cost.DispatchCost,
            cost.PackagingCost,
            cost.HandlingCost,
            cost.PickingCost,
            cost.SplitPenalty,
            cost.InventoryReliabilityPenalty,
            cost.ScarcityPenalty,
            cost.SlaPenalty,
            cost.TotalCost);
}

public sealed record OptimizedPlanResponse(
    string Strategy,
    string Status,
    string StrategyUsed,
    IReadOnlyList<SourcingWarehouseAssignmentResponse> Warehouses,
    int ShipmentCount,
    decimal TotalDistanceKm,
    decimal TotalDurationMinutes,
    CostBreakdownResponse Cost,
    string RouteSource,
    bool CompleteCoverage,
    IReadOnlyList<string> Explanations,
    double EvaluationTimeMs)
{
    public static OptimizedPlanResponse From(Wms.Modules.Fulfillment.Application.Optimization.OptimizedPlan plan) =>
        new(
            plan.Strategy.ToString(),
            plan.Status.ToString(),
            plan.StrategyUsed,
            plan.Warehouses.Select(SourcingWarehouseAssignmentResponse.From).ToList(),
            plan.ShipmentCount,
            plan.TotalDistanceKm,
            plan.TotalDurationMinutes,
            CostBreakdownResponse.From(plan.Cost),
            plan.RouteSource,
            plan.CompleteCoverage,
            plan.Explanations,
            plan.EvaluationTime.TotalMilliseconds);
}

public sealed record StrategyComparisonResponse(
    OptimizedPlanResponse? Nearest,
    OptimizedPlanResponse? Greedy,
    OptimizedPlanResponse? Optimized,
    string RecommendedStrategy,
    decimal? SavingsVsNearest,
    IReadOnlyList<string> Counterfactuals)
{
    public static StrategyComparisonResponse From(Wms.Modules.Fulfillment.Application.Optimization.StrategyComparison comparison) =>
        new(
            comparison.Nearest is null ? null : OptimizedPlanResponse.From(comparison.Nearest),
            comparison.Greedy is null ? null : OptimizedPlanResponse.From(comparison.Greedy),
            comparison.Optimized is null ? null : OptimizedPlanResponse.From(comparison.Optimized),
            comparison.RecommendedStrategy,
            comparison.SavingsVsNearest,
            comparison.Counterfactuals);
}

public sealed record EvaluateSourcingResponse(
    Guid SourcingRequestId,
    bool Fulfillable,
    IReadOnlyList<SourcingCandidateResponse> Candidates,
    IReadOnlyList<SourcingShortageResponse> Shortages,
    IReadOnlyList<SourcingIncomingStockResponse> IncomingStock,
    OptimizedPlanResponse? Optimization,
    StrategyComparisonResponse? Comparison)
{
    public static EvaluateSourcingResponse From(SourcingEvaluation evaluation) =>
        new(
            evaluation.SourcingRequestId,
            evaluation.Fulfillable,
            evaluation.Candidates.Select(SourcingCandidateResponse.From).ToList(),
            evaluation.Shortages.Select(SourcingShortageResponse.From).ToList(),
            evaluation.IncomingStock.Select(SourcingIncomingStockResponse.From).ToList(),
            evaluation.Optimization is null ? null : OptimizedPlanResponse.From(evaluation.Optimization),
            evaluation.Comparison is null ? null : StrategyComparisonResponse.From(evaluation.Comparison));
}

public sealed record CommitSourcingLineRequest(Guid SkuId, int Quantity);

public sealed record CommitSourcingWarehouseRequest(Guid WarehouseId, IReadOnlyList<CommitSourcingLineRequest> Lines);

public sealed record OptimizationSnapshotRequest(
    string StrategyUsed,
    string Status,
    decimal TotalCost,
    decimal TotalDistanceKm,
    string RouteSource,
    IReadOnlyList<string> Explanations);

public sealed record CommitSourcingRequest(
    Guid? RequestId,
    IReadOnlyList<CommitSourcingWarehouseRequest> Plan,
    OptimizationSnapshotRequest? Optimization = null);

public sealed record SourcingOrderLinkResponse(Guid WarehouseId, Guid OutboundOrderId, string OrderNumber)
{
    public static SourcingOrderLinkResponse From(SourcingOrderLinkInfo link) =>
        new(link.WarehouseId, link.OutboundOrderId, link.OrderNumber);
}

public sealed record CommitSourcingResponse(
    string Outcome,
    Guid? DecisionId,
    IReadOnlyList<SourcingOrderLinkResponse> OrderLinks,
    string? StaleReason);

public sealed record SourcingQueryLineResponse(Guid Id, Guid SkuId, int Quantity);

public sealed record SourcingQueryResponse(
    Guid Id,
    Guid RequestId,
    string Destination,
    string Status,
    DateTime CreatedAt,
    IReadOnlyList<SourcingQueryLineResponse> Lines,
    IReadOnlyList<SourcingOrderLinkResponse> OrderLinks)
{
    public static SourcingQueryResponse From(SourcingQuery query) =>
        new(
            query.Id,
            query.RequestId,
            query.Destination,
            query.Status.ToString(),
            query.CreatedAt,
            query.Lines.Select(l => new SourcingQueryLineResponse(l.Id, l.SkuId, l.Quantity)).ToList(),
            query.OrderLinks.Select(l => new SourcingOrderLinkResponse(l.WarehouseId, l.OutboundOrderId, l.OrderNumber)).ToList());
}
