using Microsoft.EntityFrameworkCore;
using Wms.Modules.Facility.Contracts;
using Wms.Modules.Facility.Infrastructure.Persistence;
using Wms.Modules.Inventory.Application;
using Wms.Modules.Inventory.Application.Accuracy;
using Wms.Modules.Inventory.Application.Accuracy.CycleCounting;
using Wms.Modules.Inventory.Application.Accuracy.Reconciliation;
using Wms.Modules.Inventory.Domain;
using Wms.Modules.Inventory.Domain.Accuracy;
using Wms.Modules.Inventory.Domain.Accuracy.CycleCounting;
using Wms.Modules.Inventory.Domain.Accuracy.Reconciliation;
using Wms.Modules.Inventory.Infrastructure.Persistence;
using Wms.Modules.MasterData.Contracts;
using Wms.Modules.MasterData.Infrastructure.Persistence;
using Xunit;

namespace Wms.InventoryTests.Persistence;

public sealed class ReconciliationIntegrationTests
{
    private static async Task<(
        InventoryStore Store,
        InventoryDbContext Db,
        Guid Sku,
        Guid Warehouse,
        Guid Location,
        StartCycleCount Start,
        CompleteCycleCount Complete,
        ApproveReconciliation Approve,
        RejectReconciliation Reject)> CreateReconciliationWorldAsync(
        int openingQuantity = 100,
        int? largeVarianceThreshold = null)
    {
        var sku = await Db.CreateSkuAsync();
        var (warehouse, location) = await Db.CreateWarehouseWithStorageLocationAsync();

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

        var policy = new RiskPolicyOptions();
        if (largeVarianceThreshold.HasValue)
        {
            policy.LargeVarianceThreshold = largeVarianceThreshold.Value;
        }

        var analyzer = new InventoryRiskAnalyzer(policy);
        var start = new StartCycleCount(store);
        var complete = new CompleteCycleCount(store, analyzer);
        var approve = new ApproveReconciliation(store);
        var reject = new RejectReconciliation(store);

        return (store, inventoryDb, sku, warehouse, location, start, complete, approve, reject);
    }

    private static async Task<CycleCountTask> CreateAndStartTaskAsync(
        InventoryDbContext db, StartCycleCount start, Guid warehouse, Guid location, Guid sku)
    {
        var task = CycleCountTask.Create(
            warehouse, location, sku, CycleCountReason.HighRisk, CycleCountPriority.High, 90, "test evidence");
        db.Add(task);
        await db.SaveChangesAsync();
        await start.Handle(task.Id, null, CancellationToken.None);
        return task;
    }

    // 1 — Variance zero → no reconciliation.
    [Fact]
    public async Task Zero_variance_creates_no_reconciliation()
    {
        var (store, db, sku, warehouse, location, start, complete, _, _) = await CreateReconciliationWorldAsync(8);
        var task = await CreateAndStartTaskAsync(db, start, warehouse, location, sku);

        var result = await complete.Handle(task.Id, 8, null, CancellationToken.None);

        Assert.Equal(CountOutcome.Verified, result.Outcome);
        var reconciliation = await store.GetReconciliationByResultIdAsync(result.Id, CancellationToken.None);
        Assert.Null(reconciliation);
    }

    // 2 — Negative variance creates reconciliation.
    [Fact]
    public async Task Negative_variance_creates_reconciliation()
    {
        var (store, db, sku, warehouse, location, start, complete, _, _) = await CreateReconciliationWorldAsync(8);
        var task = await CreateAndStartTaskAsync(db, start, warehouse, location, sku);

        var result = await complete.Handle(task.Id, 3, null, CancellationToken.None);

        var reconciliation = await store.GetReconciliationByResultIdAsync(result.Id, CancellationToken.None);
        Assert.NotNull(reconciliation);
        Assert.Equal(-5, reconciliation!.Variance);
        Assert.Equal(ReconciliationStatus.Open, reconciliation.ReconciliationStatus);
        Assert.Equal(AdjustmentReason.CycleCountVariance, reconciliation.Reason);
    }

    // 3 — Positive variance creates reconciliation.
    [Fact]
    public async Task Positive_variance_creates_reconciliation()
    {
        var (store, db, sku, warehouse, location, start, complete, _, _) = await CreateReconciliationWorldAsync(5);
        var task = await CreateAndStartTaskAsync(db, start, warehouse, location, sku);

        var result = await complete.Handle(task.Id, 7, null, CancellationToken.None);

        var reconciliation = await store.GetReconciliationByResultIdAsync(result.Id, CancellationToken.None);
        Assert.NotNull(reconciliation);
        Assert.Equal(2, reconciliation!.Variance);
    }

    // 4 — Duplicate reconciliation for same result is prevented by DB.
    [Fact]
    public async Task Duplicate_reconciliation_for_same_result_is_rejected()
    {
        var (store, db, sku, warehouse, location, start, complete, _, _) = await CreateReconciliationWorldAsync(8);
        var task = await CreateAndStartTaskAsync(db, start, warehouse, location, sku);
        var result = await complete.Handle(task.Id, 3, null, CancellationToken.None);

        var duplicate = InventoryReconciliation.Create(
            task.Id, result.Id, sku, warehouse, location, InventoryStatus.Available, 8, 3, -5, AdjustmentReason.CycleCountVariance, false);
        db.Add(duplicate);

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    // 5 — Approve changes balance by delta.
    [Fact]
    public async Task Approve_changes_balance_by_delta()
    {
        var (store, db, sku, warehouse, location, start, complete, approve, _) = await CreateReconciliationWorldAsync(8);
        var task = await CreateAndStartTaskAsync(db, start, warehouse, location, sku);
        await complete.Handle(task.Id, 3, null, CancellationToken.None);
        var reconciliation = await store.GetReconciliationByResultIdAsync((await store.GetCycleCountResultAsync(task.Id, CancellationToken.None))!.Id, CancellationToken.None);

        var result = await approve.Handle(
            new ApproveReconciliationCommand(reconciliation!.Id, Guid.NewGuid(), AdjustmentReason.CycleCountVariance, "supervisor", "sayım doğrulandı", false),
            CancellationToken.None);

        Assert.Equal(ApprovalOutcome.Applied, result.Outcome);
        var balance = await store.GetBalanceAsync(warehouse, sku, location, InventoryStatus.Available, CancellationToken.None);
        Assert.Equal(3, balance!.Quantity);
    }

    // 6 — Adjustment creates ledger entry.
    [Fact]
    public async Task Adjustment_writes_ledger_entry()
    {
        var (store, db, sku, warehouse, location, start, complete, approve, _) = await CreateReconciliationWorldAsync(8);
        var task = await CreateAndStartTaskAsync(db, start, warehouse, location, sku);
        await complete.Handle(task.Id, 3, null, CancellationToken.None);
        var reconciliation = await store.GetReconciliationByResultIdAsync((await store.GetCycleCountResultAsync(task.Id, CancellationToken.None))!.Id, CancellationToken.None);
        var approvalRequestId = Guid.NewGuid();

        await approve.Handle(
            new ApproveReconciliationCommand(reconciliation!.Id, approvalRequestId, AdjustmentReason.CycleCountVariance, "supervisor", null, false),
            CancellationToken.None);

        var ledger = await store.ListLedgerAsync(warehouse, sku, location, 20, CancellationToken.None);
        Assert.Contains(ledger, e => e.EntryType == LedgerEntryType.InventoryAdjustment && e.QuantityDelta == -5 && e.RequestId == approvalRequestId);
    }

    // 7 — Reconciliation + balance + ledger + adjustment + signal atomic.
    [Fact]
    public async Task Approval_persists_all_records_atomically()
    {
        var (store, db, sku, warehouse, location, start, complete, approve, _) = await CreateReconciliationWorldAsync(8);
        var task = await CreateAndStartTaskAsync(db, start, warehouse, location, sku);
        await complete.Handle(task.Id, 3, null, CancellationToken.None);
        var reconciliation = await store.GetReconciliationByResultIdAsync((await store.GetCycleCountResultAsync(task.Id, CancellationToken.None))!.Id, CancellationToken.None);

        await approve.Handle(
            new ApproveReconciliationCommand(reconciliation!.Id, Guid.NewGuid(), AdjustmentReason.CycleCountVariance, "supervisor", "note", false),
            CancellationToken.None);

        var fresh = await store.GetReconciliationAsync(reconciliation.Id, CancellationToken.None);
        Assert.Equal(ReconciliationStatus.Approved, fresh!.ReconciliationStatus);

        var adjustment = await store.GetAdjustmentByReconciliationIdAsync(reconciliation.Id, CancellationToken.None);
        Assert.NotNull(adjustment);
        Assert.Equal(-5, adjustment!.QuantityDelta);
        Assert.Equal(AdjustmentReason.CycleCountVariance, adjustment.Reason);

        var signals = await store.ListAccuracySignalsAsync(null, sku, location, null, null, null, 20, CancellationToken.None);
        Assert.Contains(signals, s => s.SignalType == AccuracySignalType.DiscrepancyConfirmed);
    }

    // 8 — Duplicate approve applies adjustment once.
    [Fact]
    public async Task Duplicate_approve_applies_adjustment_once()
    {
        var (store, db, sku, warehouse, location, start, complete, approve, _) = await CreateReconciliationWorldAsync(8);
        var task = await CreateAndStartTaskAsync(db, start, warehouse, location, sku);
        await complete.Handle(task.Id, 3, null, CancellationToken.None);
        var reconciliation = await store.GetReconciliationByResultIdAsync((await store.GetCycleCountResultAsync(task.Id, CancellationToken.None))!.Id, CancellationToken.None);
        var approvalRequestId = Guid.NewGuid();

        var first = await approve.Handle(new ApproveReconciliationCommand(reconciliation!.Id, approvalRequestId, AdjustmentReason.CycleCountVariance, null, null, false), CancellationToken.None);
        var second = await approve.Handle(new ApproveReconciliationCommand(reconciliation.Id, approvalRequestId, AdjustmentReason.CycleCountVariance, null, null, false), CancellationToken.None);

        Assert.Equal(ApprovalOutcome.Applied, first.Outcome);
        Assert.Equal(ApprovalOutcome.AlreadyApproved, second.Outcome);

        var balance = await store.GetBalanceAsync(warehouse, sku, location, InventoryStatus.Available, CancellationToken.None);
        Assert.Equal(3, balance!.Quantity);
        var adjustmentCount = await db.InventoryAdjustments.CountAsync(a => a.ReconciliationId == reconciliation.Id);
        Assert.Equal(1, adjustmentCount);
    }

    // 9 — Reject does not change stock.
    [Fact]
    public async Task Reject_does_not_change_stock()
    {
        var (store, db, sku, warehouse, location, start, complete, _, reject) = await CreateReconciliationWorldAsync(8);
        var task = await CreateAndStartTaskAsync(db, start, warehouse, location, sku);
        await complete.Handle(task.Id, 3, null, CancellationToken.None);
        var reconciliation = await store.GetReconciliationByResultIdAsync((await store.GetCycleCountResultAsync(task.Id, CancellationToken.None))!.Id, CancellationToken.None);

        await reject.Handle(reconciliation!.Id, "supervisor", "yanlış lokasyon sayıldı", CancellationToken.None);

        var balance = await store.GetBalanceAsync(warehouse, sku, location, InventoryStatus.Available, CancellationToken.None);
        Assert.Equal(8, balance!.Quantity);
        var ledger = await store.ListLedgerAsync(warehouse, sku, location, 20, CancellationToken.None);
        Assert.DoesNotContain(ledger, e => e.EntryType == LedgerEntryType.InventoryAdjustment);
        var fresh = await store.GetReconciliationAsync(reconciliation.Id, CancellationToken.None);
        Assert.Equal(ReconciliationStatus.Rejected, fresh!.ReconciliationStatus);
    }

    // 10 — Stale snapshot blocks adjustment.
    [Fact]
    public async Task Stale_snapshot_blocks_adjustment()
    {
        var (store, db, sku, warehouse, location, start, complete, approve, _) = await CreateReconciliationWorldAsync(8);
        var task = await CreateAndStartTaskAsync(db, start, warehouse, location, sku);
        await complete.Handle(task.Id, 3, null, CancellationToken.None);
        var reconciliation = await store.GetReconciliationByResultIdAsync((await store.GetCycleCountResultAsync(task.Id, CancellationToken.None))!.Id, CancellationToken.None);

        var destination = await AddLocationAsync(warehouse);
        var relocate = new RelocateStock(store, new MasterDataQueryContract(Db.CreateMasterDataContext()), new FacilityQueryContract(Db.CreateFacilityContext()));
        await relocate.Handle(new RelocateCommand(Guid.NewGuid(), sku, warehouse, location, destination, 2), CancellationToken.None);

        var result = await approve.Handle(
            new ApproveReconciliationCommand(reconciliation!.Id, Guid.NewGuid(), AdjustmentReason.CycleCountVariance, null, null, false),
            CancellationToken.None);

        Assert.Equal(ApprovalOutcome.Stale, result.Outcome);
        var balance = await store.GetBalanceAsync(warehouse, sku, location, InventoryStatus.Available, CancellationToken.None);
        Assert.Equal(6, balance!.Quantity); // 8 - 2 (relocate) — adjustment uygulanmadı
    }

    // 11 — Correction that would break Allocated <= Quantity is rejected.
    [Fact]
    public async Task Correction_breaking_allocated_invariant_is_rejected()
    {
        var (store, db, sku, warehouse, location, start, complete, approve, _) = await CreateReconciliationWorldAsync(10);
        var task = await CreateAndStartTaskAsync(db, start, warehouse, location, sku);

        var reserve = new Reserve(store, new MasterDataQueryContract(Db.CreateMasterDataContext()), new FacilityQueryContract(Db.CreateFacilityContext()));
        await reserve.Handle(new ReserveCommand(Guid.NewGuid(), sku, warehouse, 8, "order"), CancellationToken.None);

        await complete.Handle(task.Id, 5, null, CancellationToken.None);
        var reconciliation = await store.GetReconciliationByResultIdAsync((await store.GetCycleCountResultAsync(task.Id, CancellationToken.None))!.Id, CancellationToken.None);

        await Assert.ThrowsAsync<AdjustmentConflictException>(() => approve.Handle(
            new ApproveReconciliationCommand(reconciliation!.Id, Guid.NewGuid(), AdjustmentReason.CycleCountVariance, null, null, false),
            CancellationToken.None));

        var balance = await store.GetBalanceAsync(warehouse, sku, location, InventoryStatus.Available, CancellationToken.None);
        Assert.Equal(10, balance!.Quantity);
        Assert.Equal(8, balance.Allocated);
        var fresh = await store.GetReconciliationAsync(reconciliation!.Id, CancellationToken.None);
        Assert.Equal(ReconciliationStatus.Open, fresh!.ReconciliationStatus);
    }

    // 13 — Status partition is preserved (only AVAILABLE affected).
    [Fact]
    public async Task Adjustment_only_affects_target_status_partition()
    {
        var (store, db, sku, warehouse, location, start, complete, approve, _) = await CreateReconciliationWorldAsync(8);

        var facilityDb = Db.CreateFacilityContext();
        var masterDb = Db.CreateMasterDataContext();
        var opening = new RecordOpeningBalance(store, new MasterDataQueryContract(masterDb), new FacilityQueryContract(facilityDb));
        await opening.Handle(new RecordOpeningBalanceCommand(Guid.NewGuid(), sku, warehouse, location, InventoryStatus.Hold, 5), CancellationToken.None);

        var task = await CreateAndStartTaskAsync(db, start, warehouse, location, sku);
        await complete.Handle(task.Id, 3, null, CancellationToken.None);
        var reconciliation = await store.GetReconciliationByResultIdAsync((await store.GetCycleCountResultAsync(task.Id, CancellationToken.None))!.Id, CancellationToken.None);

        await approve.Handle(new ApproveReconciliationCommand(reconciliation!.Id, Guid.NewGuid(), AdjustmentReason.CycleCountVariance, null, null, false), CancellationToken.None);

        var available = await store.GetBalanceAsync(warehouse, sku, location, InventoryStatus.Available, CancellationToken.None);
        var hold = await store.GetBalanceAsync(warehouse, sku, location, InventoryStatus.Hold, CancellationToken.None);
        Assert.Equal(3, available!.Quantity);
        Assert.Equal(5, hold!.Quantity);
    }

    // 14 — Historical reconciliation is immutable.
    [Fact]
    public void Reconciliation_exposes_no_public_setters()
    {
        var publicSetters = typeof(InventoryReconciliation)
            .GetProperties()
            .Where(p => p.SetMethod is { IsPublic: true })
            .Select(p => p.Name)
            .ToList();

        Assert.Empty(publicSetters);
    }

    // 16 — Large variance policy requires force.
    [Fact]
    public async Task Large_variance_requires_force()
    {
        var (store, db, sku, warehouse, location, start, complete, approve, _) = await CreateReconciliationWorldAsync(50, largeVarianceThreshold: 10);
        var task = await CreateAndStartTaskAsync(db, start, warehouse, location, sku);
        await complete.Handle(task.Id, 5, null, CancellationToken.None);
        var reconciliation = await store.GetReconciliationByResultIdAsync((await store.GetCycleCountResultAsync(task.Id, CancellationToken.None))!.Id, CancellationToken.None);

        Assert.True(reconciliation!.IsLargeVariance);

        await Assert.ThrowsAsync<LargeVarianceException>(() => approve.Handle(
            new ApproveReconciliationCommand(reconciliation.Id, Guid.NewGuid(), AdjustmentReason.CycleCountVariance, null, null, false),
            CancellationToken.None));

        var forced = await approve.Handle(
            new ApproveReconciliationCommand(reconciliation.Id, Guid.NewGuid(), AdjustmentReason.CycleCountVariance, null, "forced", true),
            CancellationToken.None);

        Assert.Equal(ApprovalOutcome.Applied, forced.Outcome);
    }

    // 17 — Confirmed discrepancy is readable as future accuracy evidence.
    [Fact]
    public async Task Confirmed_discrepancy_is_readable_as_accuracy_signal()
    {
        var (store, db, sku, warehouse, location, start, complete, approve, _) = await CreateReconciliationWorldAsync(8);
        var task = await CreateAndStartTaskAsync(db, start, warehouse, location, sku);
        await complete.Handle(task.Id, 2, null, CancellationToken.None);
        var reconciliation = await store.GetReconciliationByResultIdAsync((await store.GetCycleCountResultAsync(task.Id, CancellationToken.None))!.Id, CancellationToken.None);

        await approve.Handle(new ApproveReconciliationCommand(reconciliation!.Id, Guid.NewGuid(), AdjustmentReason.CycleCountVariance, null, null, false), CancellationToken.None);

        var signals = await store.ListAccuracySignalsAsync(
            warehouse, sku, location, AccuracySignalType.DiscrepancyConfirmed, null, null, 10, CancellationToken.None);
        var signal = Assert.Single(signals);
        Assert.Equal(AccuracySourceType.CycleCount, signal.SourceType);
        Assert.Equal(2, signal.SystemQuantityAtSignal); // post-adjustment snapshot
    }

    private static async Task<Guid> AddLocationAsync(Guid warehouseId)
    {
        await using var db = Db.CreateFacilityContext();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var location = Wms.Modules.Facility.Domain.Location.Create(
            warehouseId, null, $"REC-{suffix}", "Reconciliation Lokasyonu", Wms.Modules.Facility.Domain.LocationType.Storage, holdsInventory: true);
        db.Add(location);
        await db.SaveChangesAsync();
        return location.Id;
    }
}

public sealed class AdjustmentDomainTests
{
    [Fact]
    public void ApplyAdjustment_rejects_negative_final_quantity()
    {
        var balance = Wms.Modules.Inventory.Domain.InventoryBalance.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), InventoryStatus.Available, 5);

        Assert.Throws<InvalidOperationException>(() => balance.ApplyAdjustment(-6));
    }

    [Fact]
    public void ApplyAdjustment_rejects_breaking_allocated_invariant()
    {
        var balance = Wms.Modules.Inventory.Domain.InventoryBalance.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), InventoryStatus.Available, 10);
        balance.AddAllocated(8);

        Assert.Throws<InvalidOperationException>(() => balance.ApplyAdjustment(-5));
    }

    [Fact]
    public void InventoryReconciliation_rejects_zero_variance()
    {
        Assert.Throws<ArgumentException>(() => InventoryReconciliation.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            InventoryStatus.Available, 8, 8, 0, AdjustmentReason.CycleCountVariance, false));
    }

    [Fact]
    public void InventoryAdjustment_rejects_zero_delta()
    {
        Assert.Throws<ArgumentException>(() => InventoryAdjustment.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            InventoryStatus.Available, 0, AdjustmentReason.CycleCountVariance, null, null));
    }
}
