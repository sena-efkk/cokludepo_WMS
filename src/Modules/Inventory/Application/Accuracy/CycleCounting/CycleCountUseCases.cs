using Wms.Modules.Inventory.Domain;
using Wms.Modules.Inventory.Domain.Accuracy;
using Wms.Modules.Inventory.Domain.Accuracy.CycleCounting;
using Wms.Modules.Inventory.Domain.Accuracy.Reconciliation;
using Wms.Integration.Telemetry;

namespace Wms.Modules.Inventory.Application.Accuracy.CycleCounting;

public sealed record EvaluateCycleCountsResult(int Created, int Skipped);

public sealed class EvaluateCycleCountCandidates(
    IInventoryStore store,
    ListRiskAssessments listRiskAssessments,
    InventoryRiskAnalyzer analyzer)
{
    public async Task<EvaluateCycleCountsResult> Handle(Guid? warehouseId, CancellationToken cancellationToken)
    {
        var assessments = await listRiskAssessments.Handle(warehouseId, null, null, null, 10_000, cancellationToken);

        var created = 0;
        var skipped = 0;

        var activePairsByWarehouse = new Dictionary<Guid, HashSet<(Guid SkuId, Guid LocationId)>>();

        foreach (var assessment in assessments.OrderByDescending(a => a.RiskScore))
        {
            var repeated = assessment.ConsecutiveNotFound >= analyzer.Options.RepeatedNotFoundThreshold;
            if (!repeated && assessment.RiskLevel != RiskLevel.Red)
            {
                continue;
            }

            if (!activePairsByWarehouse.TryGetValue(assessment.WarehouseId, out var activePairs))
            {
                var activeTasks = await store.GetActiveCycleCountTasksAsync(assessment.WarehouseId, cancellationToken);
                activePairs = activeTasks.Select(t => (t.SkuId, t.LocationId)).ToHashSet();
                activePairsByWarehouse[assessment.WarehouseId] = activePairs;
            }

            if (activePairs.Contains((assessment.SkuId, assessment.LocationId)))
            {
                skipped++;
                continue;
            }

            var reason = repeated ? CycleCountReason.RepeatedNotFound : CycleCountReason.HighRisk;
            var priority = repeated && assessment.RiskLevel == RiskLevel.Red
                ? CycleCountPriority.Critical
                : assessment.RiskLevel == RiskLevel.Red
                    ? CycleCountPriority.High
                    : CycleCountPriority.Medium;

            var evidence = string.Join("; ", assessment.Reasons.Select(r => $"{r.Code}: {r.Description}"));
            var task = CycleCountTask.Create(
                assessment.WarehouseId,
                assessment.LocationId,
                assessment.SkuId,
                reason,
                priority,
                assessment.RiskScore,
                evidence);

            await store.AddCycleCountTaskAsync(task, cancellationToken);
            var outcome = await store.SaveChangesAsync(cancellationToken);
            if (outcome == StoreSaveOutcome.DuplicateRequest)
            {
                skipped++;
            }
            else
            {
                created++;
            }
        }

        WmsMetrics.CycleCountsCreatedTotal.Add(created);
        return new EvaluateCycleCountsResult(created, skipped);
    }
}

public sealed class StartCycleCount(IInventoryStore store)
{
    public async Task<CycleCountTask> Handle(Guid taskId, string? assignedTo, CancellationToken cancellationToken)
    {
        var task = await store.GetCycleCountTaskAsync(taskId, cancellationToken)
            ?? throw new CycleCountTaskNotFoundException(taskId);

        var balance = await store.GetBalanceAsync(
            task.WarehouseId,
            task.SkuId,
            task.LocationId,
            InventoryStatus.Available,
            cancellationToken);

        task.Start(
            balance?.Quantity ?? 0,
            balance?.Allocated ?? 0,
            InventoryStatus.Available,
            assignedTo);

        await store.SaveChangesAsync(cancellationToken);
        return task;
    }
}

public sealed class CompleteCycleCount(IInventoryStore store, InventoryRiskAnalyzer analyzer)
{
    public async Task<CycleCountResult> Handle(
        Guid taskId,
        int countedQuantity,
        string? countedBy,
        CancellationToken cancellationToken)
    {
        var task = await store.GetCycleCountTaskAsync(taskId, cancellationToken)
            ?? throw new CycleCountTaskNotFoundException(taskId);

        if (task.Status != CycleCountTaskStatus.InProgress)
        {
            throw new InvalidCycleCountStateException(
                $"Yalnızca IN_PROGRESS task tamamlanabilir. Mevcut: {task.Status}.");
        }

        var activityMap = await store.GetPhysicalActivityAsync(task.WarehouseId, task.SkuId, cancellationToken);
        var lastMovementAt = activityMap.FirstOrDefault(a => a.LocationId == task.LocationId)?.LastAt;

        var stale = lastMovementAt.HasValue
            && task.StartedAt.HasValue
            && lastMovementAt.Value > task.StartedAt.Value;

        var variance = countedQuantity - (task.ExpectedQuantity ?? 0);
        var outcome = stale
            ? CountOutcome.StaleRecountRequired
            : variance == 0
                ? CountOutcome.Verified
                : CountOutcome.VarianceDetected;

        var result = CycleCountResult.Create(
            task.Id,
            countedQuantity,
            countedBy,
            DateTime.UtcNow,
            task.ExpectedQuantity ?? 0,
            task.ExpectedAllocated ?? 0,
            task.ExpectedStatus ?? InventoryStatus.Available,
            variance,
            outcome);

        task.Complete();

        if (outcome == CountOutcome.VarianceDetected)
        {
            var isLarge = Math.Abs(variance) >= analyzer.Options.LargeVarianceThreshold;
            var reconciliation = InventoryReconciliation.Create(
                task.Id,
                result.Id,
                task.SkuId,
                task.WarehouseId,
                task.LocationId,
                task.ExpectedStatus ?? InventoryStatus.Available,
                task.ExpectedQuantity ?? 0,
                countedQuantity,
                variance,
                AdjustmentReason.CycleCountVariance,
                isLarge);

            await store.AddReconciliationAsync(reconciliation, cancellationToken);
        }

        await store.AddCycleCountResultAsync(result, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        return result;
    }
}

public sealed class CancelCycleCount(IInventoryStore store)
{
    public async Task Handle(Guid taskId, CancellationToken cancellationToken)
    {
        var task = await store.GetCycleCountTaskAsync(taskId, cancellationToken)
            ?? throw new CycleCountTaskNotFoundException(taskId);

        task.Cancel();
        await store.SaveChangesAsync(cancellationToken);
    }
}

public sealed class GetCycleCountTask(IInventoryStore store)
{
    public async Task<CycleCountTask?> Handle(Guid taskId, CancellationToken cancellationToken)
    {
        return await store.GetCycleCountTaskAsync(taskId, cancellationToken);
    }
}

public sealed class GetCycleCountResult(IInventoryStore store)
{
    public async Task<CycleCountResult?> Handle(Guid taskId, CancellationToken cancellationToken)
    {
        return await store.GetCycleCountResultAsync(taskId, cancellationToken);
    }
}

public sealed class ListCycleCountTasks(IInventoryStore store)
{
    public async Task<IReadOnlyList<CycleCountTask>> Handle(
        Guid? warehouseId,
        CycleCountTaskStatus? status,
        CycleCountPriority? priority,
        int limit,
        CancellationToken cancellationToken)
    {
        return await store.ListCycleCountTasksAsync(warehouseId, status, priority, limit, cancellationToken);
    }
}

public sealed class GetCycleCountQueue(IInventoryStore store)
{
    public async Task<IReadOnlyList<CycleCountTask>> Handle(Guid? warehouseId, int limit, CancellationToken cancellationToken)
    {
        return await store.GetCycleCountQueueAsync(warehouseId, limit, cancellationToken);
    }
}

public sealed class CycleCountTaskNotFoundException : InventoryNotFoundException
{
    public CycleCountTaskNotFoundException(Guid taskId)
        : base($"Cycle count task bulunamadı: {taskId}")
    {
    }
}

public sealed class InvalidCycleCountStateException : Exception
{
    public InvalidCycleCountStateException(string message)
        : base(message)
    {
    }
}
