using Wms.Modules.Inventory.Domain.Accuracy.Reconciliation;

namespace Wms.Modules.Inventory.Application.Accuracy.Reconciliation;

public sealed class ReconciliationNotFoundException : InventoryNotFoundException
{
    public ReconciliationNotFoundException(Guid reconciliationId)
        : base($"Reconciliation bulunamadı: {reconciliationId}")
    {
    }
}

public sealed class InvalidReconciliationStateException : Exception
{
    public InvalidReconciliationStateException(string message)
        : base(message)
    {
    }
}

public sealed class AdjustmentConflictException : Exception
{
    public AdjustmentConflictException(string message)
        : base(message)
    {
    }
}

public sealed class LargeVarianceException : Exception
{
    public LargeVarianceException(string message)
        : base(message)
    {
    }
}

public enum ApprovalOutcome
{
    Applied = 1,
    AlreadyApproved = 2,
    Stale = 3,
}

public sealed record ApprovalResult(ApprovalOutcome Outcome, Guid? AdjustmentId);

public sealed record ApproveReconciliationCommand(
    Guid ReconciliationId,
    Guid RequestId,
    AdjustmentReason Reason,
    string? ResolvedBy,
    string? ResolutionNote,
    bool Force);

public sealed class ApproveReconciliation(IInventoryStore store)
{
    public async Task<ApprovalResult> Handle(ApproveReconciliationCommand command, CancellationToken cancellationToken)
    {
        var reconciliation = await store.GetReconciliationAsync(command.ReconciliationId, cancellationToken)
            ?? throw new ReconciliationNotFoundException(command.ReconciliationId);

        if (reconciliation.ReconciliationStatus == ReconciliationStatus.Approved)
        {
            var existingAdjustment = await store.GetAdjustmentByReconciliationIdAsync(reconciliation.Id, cancellationToken);
            return new ApprovalResult(ApprovalOutcome.AlreadyApproved, existingAdjustment?.Id);
        }

        if (reconciliation.ReconciliationStatus != ReconciliationStatus.Open)
        {
            throw new InvalidReconciliationStateException(
                $"Yalnızca OPEN reconciliation approve edilebilir. Mevcut: {reconciliation.ReconciliationStatus}.");
        }

        if (reconciliation.IsLargeVariance && !command.Force)
        {
            throw new LargeVarianceException(
                $"Büyük variance ({reconciliation.Variance}) manuel inceleme gerektirir — force=true ile onaylayabilirsiniz.");
        }

        if (command.Reason == AdjustmentReason.Other && string.IsNullOrWhiteSpace(command.ResolutionNote))
        {
            throw new ArgumentException("Reason=OTHER seçildiğinde açıklama zorunludur.", nameof(command.ResolutionNote));
        }

        var outcome = await store.ExecuteReconciliationApprovalAsync(
            reconciliation.Id,
            command.RequestId,
            reconciliation.Variance,
            command.Reason,
            command.ResolvedBy,
            command.ResolutionNote,
            cancellationToken);

        if (outcome == ApprovalOutcome.AlreadyApproved)
        {
            var existingAdjustment = await store.GetAdjustmentByReconciliationIdAsync(reconciliation.Id, cancellationToken);
            return new ApprovalResult(ApprovalOutcome.AlreadyApproved, existingAdjustment?.Id);
        }

        if (outcome == ApprovalOutcome.Stale)
        {
            return new ApprovalResult(ApprovalOutcome.Stale, null);
        }

        var adjustment = await store.GetAdjustmentByReconciliationIdAsync(reconciliation.Id, cancellationToken);
        return new ApprovalResult(ApprovalOutcome.Applied, adjustment?.Id);
    }
}

public sealed class RejectReconciliation(IInventoryStore store)
{
    public async Task Handle(Guid reconciliationId, string? resolvedBy, string? resolutionNote, CancellationToken cancellationToken)
    {
        var reconciliation = await store.GetReconciliationAsync(reconciliationId, cancellationToken)
            ?? throw new ReconciliationNotFoundException(reconciliationId);

        if (reconciliation.ReconciliationStatus == ReconciliationStatus.Rejected)
        {
            return;
        }

        reconciliation.Reject(resolvedBy, resolutionNote);
        await store.SaveChangesAsync(cancellationToken);
    }
}

public sealed class CancelReconciliation(IInventoryStore store)
{
    public async Task Handle(Guid reconciliationId, string? resolvedBy, string? resolutionNote, CancellationToken cancellationToken)
    {
        var reconciliation = await store.GetReconciliationAsync(reconciliationId, cancellationToken)
            ?? throw new ReconciliationNotFoundException(reconciliationId);

        if (reconciliation.ReconciliationStatus == ReconciliationStatus.Cancelled)
        {
            return;
        }

        reconciliation.Cancel(resolvedBy, resolutionNote);
        await store.SaveChangesAsync(cancellationToken);
    }
}

public sealed class GetReconciliation(IInventoryStore store)
{
    public async Task<InventoryReconciliation?> Handle(Guid reconciliationId, CancellationToken cancellationToken)
    {
        return await store.GetReconciliationAsync(reconciliationId, cancellationToken);
    }
}

public sealed class ListReconciliations(IInventoryStore store)
{
    public async Task<IReadOnlyList<InventoryReconciliation>> Handle(
        Guid? warehouseId,
        ReconciliationStatus? status,
        int limit,
        CancellationToken cancellationToken)
    {
        return await store.ListReconciliationsAsync(warehouseId, status, limit, cancellationToken);
    }
}
