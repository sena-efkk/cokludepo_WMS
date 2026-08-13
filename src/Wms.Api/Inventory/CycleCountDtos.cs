using Wms.Modules.Inventory.Domain.Accuracy.CycleCounting;

namespace Wms.Api.Inventory;

public sealed record StartCycleCountRequest(string? AssignedTo);

public sealed record CompleteCycleCountRequest(int CountedQuantity, string? CountedBy);

public sealed record CycleCountTaskResponse(
    Guid Id,
    Guid WarehouseId,
    Guid LocationId,
    Guid SkuId,
    string Reason,
    string Priority,
    int RiskScoreAtCreation,
    string Evidence,
    string Status,
    DateTime CreatedAt,
    DateTime? DueAt,
    string? AssignedTo,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    CycleCountResultResponse? Result)
{
    public static CycleCountTaskResponse From(CycleCountTask task, CycleCountResult? result) =>
        new(
            task.Id,
            task.WarehouseId,
            task.LocationId,
            task.SkuId,
            task.Reason.ToString(),
            task.Priority.ToString(),
            task.RiskScoreAtCreation,
            task.Evidence,
            task.Status.ToString(),
            task.CreatedAt,
            task.DueAt,
            task.AssignedTo,
            task.StartedAt,
            task.CompletedAt,
            result is null ? null : CycleCountResultResponse.From(result));
}

public sealed record CycleCountResultResponse(
    Guid Id,
    Guid CycleCountTaskId,
    int CountedQuantity,
    string? CountedBy,
    DateTime CountedAt,
    int ExpectedQuantity,
    int ExpectedAllocated,
    string ExpectedStatus,
    int Variance,
    string Outcome,
    bool RequiresReconciliation)
{
    public static CycleCountResultResponse From(CycleCountResult result) =>
        new(
            result.Id,
            result.CycleCountTaskId,
            result.CountedQuantity,
            result.CountedBy,
            result.CountedAt,
            result.ExpectedQuantity,
            result.ExpectedAllocated,
            result.ExpectedStatus.ToString(),
            result.Variance,
            result.Outcome.ToString(),
            result.RequiresReconciliation);
}

public sealed record EvaluateCycleCountsResponse(int Created, int Skipped);
