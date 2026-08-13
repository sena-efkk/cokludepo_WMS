using Wms.Modules.Inventory.Application.Accuracy;

namespace Wms.Api.Inventory;

public sealed record RiskReasonResponse(string Code, int Points, string Description)
{
    public static RiskReasonResponse From(RiskReason reason) => new(reason.Code, reason.Points, reason.Description);
}

public sealed record RiskAssessmentResponse(
    Guid SkuId,
    Guid WarehouseId,
    Guid LocationId,
    int MovementCount30d,
    int MovementCount90d,
    int MovementCount180d,
    DateTime? LastMovementAt,
    int? DaysSinceLastMovement,
    string VelocityClass,
    string MovementState,
    int NotFoundCount7d,
    int NotFoundCount30d,
    int ConsecutiveNotFound,
    DateTime? LastNotFoundAt,
    int RiskScore,
    string RiskLevel,
    IReadOnlyList<RiskReasonResponse> Reasons)
{
    public static RiskAssessmentResponse From(LocationRiskAssessment assessment) =>
        new(
            assessment.SkuId,
            assessment.WarehouseId,
            assessment.LocationId,
            assessment.MovementCount30d,
            assessment.MovementCount90d,
            assessment.MovementCount180d,
            assessment.LastMovementAt,
            assessment.DaysSinceLastMovement,
            assessment.VelocityClass.ToString(),
            assessment.MovementState.ToString(),
            assessment.NotFoundCount7d,
            assessment.NotFoundCount30d,
            assessment.ConsecutiveNotFound,
            assessment.LastNotFoundAt,
            assessment.RiskScore,
            assessment.RiskLevel.ToString(),
            assessment.Reasons.Select(RiskReasonResponse.From).ToList());
}

public sealed record AbcDeadSummaryResponse(
    Guid WarehouseId,
    int ClassA,
    int ClassB,
    int ClassC,
    int StateActive,
    int StateSlow,
    int StateDead)
{
    public static AbcDeadSummaryResponse From(AbcDeadSummary summary) =>
        new(summary.WarehouseId, summary.ClassA, summary.ClassB, summary.ClassC, summary.StateActive, summary.StateSlow, summary.StateDead);
}
