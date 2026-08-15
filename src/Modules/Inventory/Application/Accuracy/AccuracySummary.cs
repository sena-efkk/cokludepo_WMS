using Wms.Modules.Inventory.Application;
using Wms.Modules.Inventory.Domain.Accuracy;
using Wms.Modules.Inventory.Domain.Accuracy.CycleCounting;
using Wms.Modules.Inventory.Domain.Accuracy.Reconciliation;

namespace Wms.Modules.Inventory.Application.Accuracy;

public sealed record AccuracySummary(
    int HighRiskLocations,
    int OpenCycleCounts,
    int OpenReconciliations,
    int RecentPickNotFound);

public sealed class GetAccuracySummary(
    IInventoryStore store,
    ListRiskAssessments listRiskAssessments)
{
    public async Task<AccuracySummary> Handle(Guid? warehouseId, CancellationToken cancellationToken)
    {
        var assessments = await listRiskAssessments.Handle(warehouseId, null, null, RiskLevel.Red, 10_000, cancellationToken);
        var highRisk = assessments.Count;

        var cycleCounts = await store.ListCycleCountTasksAsync(
            warehouseId,
            null,
            null,
            10_000,
            cancellationToken);
        var openCounts = cycleCounts.Count(t => t.Status is CycleCountTaskStatus.Pending or CycleCountTaskStatus.InProgress);

        var reconciliations = await store.ListReconciliationsAsync(warehouseId, null, 10_000, cancellationToken);
        var openReconciliations = reconciliations.Count(r => r.ReconciliationStatus == ReconciliationStatus.Open);

        var signals = await store.ListAccuracySignalsAsync(warehouseId, null, null, AccuracySignalType.PickNotFound, DateTime.UtcNow.AddHours(-24), null, 10_000, cancellationToken);
        var recentNotFound = signals.Count;

        return new AccuracySummary(highRisk, openCounts, openReconciliations, recentNotFound);
    }
}
