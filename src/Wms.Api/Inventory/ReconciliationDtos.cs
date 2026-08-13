using Wms.Modules.Inventory.Domain.Accuracy.Reconciliation;

namespace Wms.Api.Inventory;

public sealed record ApproveReconciliationRequest(
    Guid? RequestId,
    string? Reason,
    string? ResolvedBy,
    string? ResolutionNote,
    bool Force);

public sealed record RejectReconciliationRequest(string? ResolvedBy, string? ResolutionNote);

public sealed record ApprovalResultResponse(string Outcome, Guid? AdjustmentId);

public sealed record ReconciliationResponse(
    Guid Id,
    Guid CycleCountTaskId,
    Guid CycleCountResultId,
    Guid SkuId,
    Guid WarehouseId,
    Guid LocationId,
    string Status,
    int ExpectedQuantity,
    int CountedQuantity,
    int Variance,
    string Reason,
    bool IsLargeVariance,
    string ReconciliationStatus,
    DateTime CreatedAt,
    DateTime? ResolvedAt,
    string? ResolvedBy,
    string ResolutionNote)
{
    public static ReconciliationResponse From(InventoryReconciliation reconciliation) =>
        new(
            reconciliation.Id,
            reconciliation.CycleCountTaskId,
            reconciliation.CycleCountResultId,
            reconciliation.SkuId,
            reconciliation.WarehouseId,
            reconciliation.LocationId,
            reconciliation.Status.ToString(),
            reconciliation.ExpectedQuantity,
            reconciliation.CountedQuantity,
            reconciliation.Variance,
            reconciliation.Reason.ToString(),
            reconciliation.IsLargeVariance,
            reconciliation.ReconciliationStatus.ToString(),
            reconciliation.CreatedAt,
            reconciliation.ResolvedAt,
            reconciliation.ResolvedBy,
            reconciliation.ResolutionNote);
}
