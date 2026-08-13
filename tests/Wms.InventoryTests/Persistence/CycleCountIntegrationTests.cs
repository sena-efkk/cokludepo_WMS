using Microsoft.EntityFrameworkCore;
using Wms.Modules.Facility.Contracts;
using Wms.Modules.Facility.Infrastructure.Persistence;
using Wms.Modules.Inventory.Application;
using Wms.Modules.Inventory.Application.Accuracy;
using Wms.Modules.Inventory.Application.Accuracy.CycleCounting;
using Wms.Modules.Inventory.Domain;
using Wms.Modules.Inventory.Domain.Accuracy;
using Wms.Modules.Inventory.Domain.Accuracy.CycleCounting;
using Wms.Modules.Inventory.Infrastructure.Persistence;
using Wms.Modules.MasterData.Contracts;
using Wms.Modules.MasterData.Infrastructure.Persistence;
using Xunit;

namespace Wms.InventoryTests.Persistence;

public sealed class CycleCountIntegrationTests
{
    private static async Task<(
        InventoryStore Store,
        InventoryDbContext Db,
        Guid Sku,
        Guid Warehouse,
        Guid Location,
        EvaluateCycleCountCandidates Evaluate,
        StartCycleCount Start,
        CompleteCycleCount Complete)> CreateCycleCountWorldAsync(bool allowsPicking = true, int openingQuantity = 100)
    {
        var sku = await Db.CreateSkuAsync();
        var (warehouse, location) = await Db.CreateWarehouseWithStorageLocationAsync(allowsPicking);

        var inventoryDb = Db.CreateInventoryContext();
        var facilityDb = Db.CreateFacilityContext();
        var masterDb = Db.CreateMasterDataContext();
        var store = new InventoryStore(inventoryDb);
        var facilityContract = new FacilityQueryContract(facilityDb);
        var masterContract = new MasterDataQueryContract(masterDb);

        var opening = new RecordOpeningBalance(store, masterContract, facilityContract);
        await opening.Handle(
            new RecordOpeningBalanceCommand(Guid.NewGuid(), sku, warehouse, location, InventoryStatus.Available, openingQuantity),
            CancellationToken.None);

        var analyzer = new InventoryRiskAnalyzer(new RiskPolicyOptions());
        var list = new ListRiskAssessments(store, facilityContract, analyzer);
        var evaluate = new EvaluateCycleCountCandidates(store, list, analyzer);
        var start = new StartCycleCount(store);
        var complete = new CompleteCycleCount(store, analyzer);

        return (store, inventoryDb, sku, warehouse, location, evaluate, start, complete);
    }

    private static async Task MakeRedAsync(InventoryStore store, InventoryDbContext db, Guid sku, Guid warehouse, Guid location)
    {
        await db.Database.ExecuteSqlAsync(
            $"UPDATE inventory.inventory_ledger SET occurred_at = now() - {400} * interval '1 day' WHERE warehouse_id = {warehouse} AND sku_id = {sku}");

        var report = new ReportPickNotFound(store, new MasterDataQueryContract(Db.CreateMasterDataContext()), new FacilityQueryContract(Db.CreateFacilityContext()));
        await report.Handle(new ReportPickNotFoundCommand(Guid.NewGuid(), sku, warehouse, location, AccuracySourceType.Pick, null, null), CancellationToken.None);
        await report.Handle(new ReportPickNotFoundCommand(Guid.NewGuid(), sku, warehouse, location, AccuracySourceType.Pick, null, null), CancellationToken.None);
    }

    // 1 â€” RED risk produces a cycle count candidate.
    [Fact]
    public async Task Red_risk_produces_cycle_count_candidate()
    {
        var (store, db, sku, warehouse, location, evaluate, _, _) = await CreateCycleCountWorldAsync();
        await MakeRedAsync(store, db, sku, warehouse, location);

        var result = await evaluate.Handle(warehouse, CancellationToken.None);

        Assert.Equal(1, result.Created);
        var tasks = await db.CycleCountTasks.Where(t => t.WarehouseId == warehouse && t.SkuId == sku && t.LocationId == location).ToListAsync();
        var task = Assert.Single(tasks);
        Assert.Equal(CycleCountReason.RepeatedNotFound, task.Reason);
        Assert.Equal(CycleCountPriority.Critical, task.Priority);
        Assert.Equal(CycleCountTaskStatus.Pending, task.Status);
        Assert.False(string.IsNullOrWhiteSpace(task.Evidence));
    }

    // 2 â€” 2 consecutive NotFound produces a task (non-red path).
    [Fact]
    public async Task Consecutive_not_found_produces_task()
    {
        var (store, _, sku, warehouse, location, evaluate, _, _) = await CreateCycleCountWorldAsync();

        var report = new ReportPickNotFound(store, new MasterDataQueryContract(Db.CreateMasterDataContext()), new FacilityQueryContract(Db.CreateFacilityContext()));
        await report.Handle(new ReportPickNotFoundCommand(Guid.NewGuid(), sku, warehouse, location, AccuracySourceType.Pick, null, null), CancellationToken.None);
        await report.Handle(new ReportPickNotFoundCommand(Guid.NewGuid(), sku, warehouse, location, AccuracySourceType.Pick, null, null), CancellationToken.None);

        var result = await evaluate.Handle(warehouse, CancellationToken.None);

        Assert.Equal(1, result.Created);
        var task = await new InventoryStore(Db.CreateInventoryContext()).GetActiveCycleCountTaskAsync(warehouse, sku, location, CancellationToken.None);
        Assert.NotNull(task);
        Assert.Equal(CycleCountReason.RepeatedNotFound, task!.Reason);
    }

    // 3 â€” GREEN risk produces no task.
    [Fact]
    public async Task Green_risk_produces_no_task()
    {
        var (store, _, sku, warehouse, location, evaluate, _, _) = await CreateCycleCountWorldAsync();

        var result = await evaluate.Handle(warehouse, CancellationToken.None);

        Assert.Equal(0, result.Created);
    }

    // 4 â€” Existing active task prevents duplicate.
    [Fact]
    public async Task Existing_active_task_prevents_duplicate()
    {
        var (store, db, sku, warehouse, location, evaluate, _, _) = await CreateCycleCountWorldAsync();
        await MakeRedAsync(store, db, sku, warehouse, location);

        var first = await evaluate.Handle(warehouse, CancellationToken.None);
        var second = await evaluate.Handle(warehouse, CancellationToken.None);

        Assert.Equal(1, first.Created);
        Assert.Equal(0, second.Created);
        Assert.Equal(1, second.Skipped);
    }

    // 5 â€” Concurrent evaluation creates exactly one task.
    [Fact]
    public async Task Concurrent_evaluation_creates_exactly_one_task()
    {
        var (store, db, sku, warehouse, location, _, _, _) = await CreateCycleCountWorldAsync();
        await MakeRedAsync(store, db, sku, warehouse, location);

        var results = await Task.WhenAll(
            RunEvaluateAsync(warehouse),
            RunEvaluateAsync(warehouse),
            RunEvaluateAsync(warehouse));

        Assert.Equal(1, results.Sum(r => r.Created));

        await using var verifyDb = Db.CreateInventoryContext();
        var active = await verifyDb.CycleCountTasks.CountAsync(t =>
            t.WarehouseId == warehouse
            && t.SkuId == sku
            && t.LocationId == location
            && (t.Status == CycleCountTaskStatus.Pending || t.Status == CycleCountTaskStatus.InProgress));
        Assert.Equal(1, active);
    }

    private static async Task<EvaluateCycleCountsResult> RunEvaluateAsync(Guid warehouseId)
    {
        await using var db = Db.CreateInventoryContext();
        await using var facilityDb = Db.CreateFacilityContext();
        var store = new InventoryStore(db);
        var facilityContract = new FacilityQueryContract(facilityDb);
        var analyzer = new InventoryRiskAnalyzer(new RiskPolicyOptions());
        var list = new ListRiskAssessments(store, facilityContract, analyzer);
        var evaluate = new EvaluateCycleCountCandidates(store, list, analyzer);
        return await evaluate.Handle(warehouseId, CancellationToken.None);
    }

    // 6 â€” Priority deterministic and explainable.
    [Fact]
    public async Task Priority_is_deterministic()
    {
        var (store, db, sku, warehouse, location, evaluate, _, _) = await CreateCycleCountWorldAsync();
        await MakeRedAsync(store, db, sku, warehouse, location);

        await evaluate.Handle(warehouse, CancellationToken.None);
        await evaluate.Handle(warehouse, CancellationToken.None);

        var task = await db.CycleCountTasks.SingleAsync(t => t.WarehouseId == warehouse && t.SkuId == sku && t.LocationId == location);
        Assert.Equal(CycleCountPriority.Critical, task.Priority);
    }

    // 7 â€” Counter does not see expected quantity in task response.
    [Fact]
    public void Task_response_dto_does_not_expose_expected_quantity()
    {
        var properties = typeof(Wms.Api.Inventory.CycleCountTaskResponse)
            .GetProperties()
            .Select(p => p.Name)
            .ToList();

        Assert.DoesNotContain("ExpectedQuantity", properties);
        Assert.DoesNotContain("ExpectedAllocated", properties);
        Assert.DoesNotContain("ExpectedStatus", properties);
    }

    // 8 â€” Expected snapshot is captured server-side at start.
    [Fact]
    public async Task Expected_snapshot_is_captured_at_start()
    {
        var (store, db, sku, warehouse, location, _, start, _) = await CreateCycleCountWorldAsync(openingQuantity: 42);
        var task = await CreatePendingTaskAsync(db, warehouse, location, sku);

        await start.Handle(task.Id, "operator-1", CancellationToken.None);

        var reloaded = await db.CycleCountTasks.SingleAsync(t => t.Id == task.Id);
        Assert.Equal(42, reloaded.ExpectedQuantity);
        Assert.Equal(0, reloaded.ExpectedAllocated);
        Assert.Equal(InventoryStatus.Available, reloaded.ExpectedStatus);
        Assert.Equal("operator-1", reloaded.AssignedTo);
        Assert.Equal(CycleCountTaskStatus.InProgress, reloaded.Status);
    }

    // 9 â€” Variance computed correctly.
    [Fact]
    public async Task Variance_is_computed_correctly()
    {
        var (_, db, sku, warehouse, location, _, start, complete) = await CreateCycleCountWorldAsync(openingQuantity: 8);
        var task = await CreatePendingTaskAsync(db, warehouse, location, sku);
        await start.Handle(task.Id, null, CancellationToken.None);

        var result = await complete.Handle(task.Id, 3, "picker-1", CancellationToken.None);

        Assert.Equal(-5, result.Variance);
        Assert.Equal(8, result.ExpectedQuantity);
        Assert.Equal(3, result.CountedQuantity);
    }

    // 10 â€” Count result does not change inventory balance.
    [Fact]
    public async Task Count_result_does_not_change_inventory_balance()
    {
        var (store, db, sku, warehouse, location, _, start, complete) = await CreateCycleCountWorldAsync(openingQuantity: 8);
        var task = await CreatePendingTaskAsync(db, warehouse, location, sku);
        await start.Handle(task.Id, null, CancellationToken.None);
        await complete.Handle(task.Id, 3, null, CancellationToken.None);

        var balance = await store.GetBalanceAsync(warehouse, sku, location, InventoryStatus.Available, CancellationToken.None);
        Assert.Equal(8, balance!.Quantity);
    }

    // 11 â€” Expected == Counted â†’ variance zero, Verified.
    [Fact]
    public async Task Equal_count_produces_verified_outcome()
    {
        var (_, db, sku, warehouse, location, _, start, complete) = await CreateCycleCountWorldAsync(openingQuantity: 8);
        var task = await CreatePendingTaskAsync(db, warehouse, location, sku);
        await start.Handle(task.Id, null, CancellationToken.None);

        var result = await complete.Handle(task.Id, 8, null, CancellationToken.None);

        Assert.Equal(0, result.Variance);
        Assert.Equal(CountOutcome.Verified, result.Outcome);
        Assert.False(result.RequiresReconciliation);
    }

    // 12 â€” Variance â†’ ReconciliationRequired.
    [Fact]
    public async Task Variance_detected_requires_reconciliation()
    {
        var (_, db, sku, warehouse, location, _, start, complete) = await CreateCycleCountWorldAsync(openingQuantity: 8);
        var task = await CreatePendingTaskAsync(db, warehouse, location, sku);
        await start.Handle(task.Id, null, CancellationToken.None);

        var result = await complete.Handle(task.Id, 2, null, CancellationToken.None);

        Assert.Equal(CountOutcome.VarianceDetected, result.Outcome);
        Assert.True(result.RequiresReconciliation);
    }

    // 13 â€” Completed task cannot be completed again.
    [Fact]
    public async Task Completed_task_cannot_be_completed_again()
    {
        var (_, db, sku, warehouse, location, _, start, complete) = await CreateCycleCountWorldAsync(openingQuantity: 8);
        var task = await CreatePendingTaskAsync(db, warehouse, location, sku);
        await start.Handle(task.Id, null, CancellationToken.None);
        await complete.Handle(task.Id, 8, null, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidCycleCountStateException>(() => complete.Handle(task.Id, 8, null, CancellationToken.None));
    }

    // 14 â€” Historical result is immutable.
    [Fact]
    public void Cycle_count_result_exposes_no_public_setters()
    {
        var publicSetters = typeof(CycleCountResult)
            .GetProperties()
            .Where(p => p.SetMethod is { IsPublic: true })
            .Select(p => p.Name)
            .ToList();

        Assert.Empty(publicSetters);
    }

    // 15 â€” Movement during count marks result stale.
    [Fact]
    public async Task Movement_during_count_marks_result_stale()
    {
        var (store, db, sku, warehouse, location, _, start, complete) = await CreateCycleCountWorldAsync(openingQuantity: 8);
        var task = await CreatePendingTaskAsync(db, warehouse, location, sku);
        await start.Handle(task.Id, null, CancellationToken.None);

        var destination = await AddLocationAsync(warehouse);
        var relocate = new RelocateStock(store, new MasterDataQueryContract(Db.CreateMasterDataContext()), new FacilityQueryContract(Db.CreateFacilityContext()));
        await relocate.Handle(new RelocateCommand(Guid.NewGuid(), sku, warehouse, location, destination, 2), CancellationToken.None);

        var result = await complete.Handle(task.Id, 6, null, CancellationToken.None);

        Assert.Equal(CountOutcome.StaleRecountRequired, result.Outcome);
        Assert.False(result.RequiresReconciliation);
    }

    // 16 â€” Successful verification breaks NotFound streak.
    [Fact]
    public async Task Successful_verification_breaks_not_found_streak()
    {
        var (store, db, sku, warehouse, location, evaluate, start, complete) = await CreateCycleCountWorldAsync(openingQuantity: 8);
        await MakeRedAsync(store, db, sku, warehouse, location);

        await evaluate.Handle(warehouse, CancellationToken.None);
        var task = await db.CycleCountTasks.SingleAsync(t => t.WarehouseId == warehouse && t.SkuId == sku && t.LocationId == location);
        await start.Handle(task.Id, null, CancellationToken.None);
        await complete.Handle(task.Id, 8, null, CancellationToken.None);

        await using var riskDb = Db.CreateInventoryContext();
        var riskStore = new InventoryStore(riskDb);
        var risk = new GetLocationRiskAssessment(riskStore, new FacilityQueryContract(Db.CreateFacilityContext()), new InventoryRiskAnalyzer(new RiskPolicyOptions()));
        var assessment = await risk.Handle(warehouse, sku, location, CancellationToken.None);

        Assert.Equal(0, assessment.ConsecutiveNotFound);
        Assert.DoesNotContain(assessment.Reasons, r => r.Code == "REPEATED_NOT_FOUND");
    }

    // 17 â€” PostgreSQL persistence (queue ordering).
    [Fact]
    public async Task Queue_orders_by_priority_desc_then_created_asc()
    {
        var (store, db, sku, warehouse, location, evaluate, _, _) = await CreateCycleCountWorldAsync();
        await MakeRedAsync(store, db, sku, warehouse, location);
        await evaluate.Handle(warehouse, CancellationToken.None);

        var secondSku = await Db.CreateSkuAsync();
        var secondLocation = await AddLocationAsync(warehouse);
        await using (var setupDb = Db.CreateInventoryContext())
        await using (var facilityDb = Db.CreateFacilityContext())
        await using (var masterDb = Db.CreateMasterDataContext())
        {
            var setupStore = new InventoryStore(setupDb);
            var opening = new RecordOpeningBalance(setupStore, new MasterDataQueryContract(masterDb), new FacilityQueryContract(facilityDb));
            await opening.Handle(new RecordOpeningBalanceCommand(Guid.NewGuid(), secondSku, warehouse, secondLocation, InventoryStatus.Available, 10), CancellationToken.None);
            await setupDb.Database.ExecuteSqlAsync(
                $"UPDATE inventory.inventory_ledger SET occurred_at = now() - {400} * interval '1 day' WHERE warehouse_id = {warehouse} AND sku_id = {secondSku}");

            var secondReport = new ReportPickNotFound(setupStore, new MasterDataQueryContract(masterDb), new FacilityQueryContract(facilityDb));
            await secondReport.Handle(new ReportPickNotFoundCommand(Guid.NewGuid(), secondSku, warehouse, secondLocation, AccuracySourceType.Pick, null, null), CancellationToken.None);
            await secondReport.Handle(new ReportPickNotFoundCommand(Guid.NewGuid(), secondSku, warehouse, secondLocation, AccuracySourceType.Pick, null, null), CancellationToken.None);
        }

        await evaluate.Handle(warehouse, CancellationToken.None);

        var queueStore = new InventoryStore(Db.CreateInventoryContext());
        var queue = await queueStore.GetCycleCountQueueAsync(warehouse, 10, CancellationToken.None);

        Assert.True(queue.Count >= 2);
        var priorities = queue.Select(t => (int)t.Priority).ToList();
        Assert.Equal(priorities.OrderByDescending(p => p), priorities);
    }

    private static async Task<CycleCountTask> CreatePendingTaskAsync(InventoryDbContext db, Guid warehouse, Guid location, Guid sku)
    {
        var task = CycleCountTask.Create(
            warehouse,
            location,
            sku,
            CycleCountReason.HighRisk,
            CycleCountPriority.High,
            90,
            "LONG_INACTIVITY: test evidence");
        db.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    private static async Task<Guid> AddLocationAsync(Guid warehouseId)
    {
        await using var db = Db.CreateFacilityContext();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var location = Wms.Modules.Facility.Domain.Location.Create(
            warehouseId, null, $"CC-{suffix}", "Cycle Count Lokasyonu", Wms.Modules.Facility.Domain.LocationType.Storage, holdsInventory: true);
        db.Add(location);
        await db.SaveChangesAsync();
        return location.Id;
    }
}
