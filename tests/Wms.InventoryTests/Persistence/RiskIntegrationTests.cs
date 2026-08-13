using Microsoft.EntityFrameworkCore;
using Wms.Modules.Facility.Contracts;
using Wms.Modules.Facility.Infrastructure.Persistence;
using Wms.Modules.Inventory.Application;
using Wms.Modules.Inventory.Application.Accuracy;
using Wms.Modules.Inventory.Domain;
using Wms.Modules.Inventory.Domain.Accuracy;
using Wms.Modules.Inventory.Infrastructure.Persistence;
using Wms.Modules.MasterData.Contracts;
using Wms.Modules.MasterData.Infrastructure.Persistence;
using Xunit;

namespace Wms.InventoryTests.Persistence;

public sealed class RiskIntegrationTests
{
    private static async Task<(
        InventoryStore Store,
        InventoryDbContext Db,
        Guid Sku,
        Guid Warehouse,
        Guid Location,
        bool AllowsPicking,
        GetLocationRiskAssessment Risk)> CreateRiskWorldAsync(bool allowsPicking = false, int openingQuantity = 100)
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

        var risk = new GetLocationRiskAssessment(store, facilityContract, new InventoryRiskAnalyzer(new RiskPolicyOptions()));
        return (store, inventoryDb, sku, warehouse, location, allowsPicking, risk);
    }

    private static async Task BackdateLedgerAsync(InventoryDbContext db, Guid warehouse, Guid sku, string interval)
    {
        await db.Database.ExecuteSqlAsync(
            $"UPDATE inventory.inventory_ledger SET occurred_at = now() - {interval}::interval WHERE warehouse_id = {warehouse} AND sku_id = {sku}");
    }

    // 4 — Reservation ledger entries do not count toward velocity.
    [Fact]
    public async Task Reservation_entries_do_not_count_toward_velocity()
    {
        var (store, db, sku, warehouse, location, _, risk) = await CreateRiskWorldAsync();

        var reserve = new Reserve(store, new MasterDataQueryContract(Db.CreateMasterDataContext()), new FacilityQueryContract(Db.CreateFacilityContext()));
        await reserve.Handle(new ReserveCommand(Guid.NewGuid(), sku, warehouse, 30, "order"), CancellationToken.None);

        var release = new ReleaseReservation(store);
        var reservations = await db.InventoryReservations
            .Where(r => r.WarehouseId == warehouse && r.SkuId == sku)
            .ToListAsync();
        await release.Handle(reservations.Single().Id, CancellationToken.None);

        var assessment = await risk.Handle(warehouse, sku, location, CancellationToken.None);

        Assert.Equal(1, assessment.MovementCount30d); // yalnızca opening balance
    }

    // 5 — Physical movement counts toward velocity.
    [Fact]
    public async Task Physical_movements_count_toward_velocity()
    {
        var (store, _, sku, warehouse, location, _, risk) = await CreateRiskWorldAsync();
        var destination = await AddLocationAsync(warehouse);

        var relocate = new RelocateStock(store, new MasterDataQueryContract(Db.CreateMasterDataContext()), new FacilityQueryContract(Db.CreateFacilityContext()));
        await relocate.Handle(new RelocateCommand(Guid.NewGuid(), sku, warehouse, location, destination, 10), CancellationToken.None);

        var assessment = await risk.Handle(warehouse, sku, location, CancellationToken.None);

        Assert.Equal(2, assessment.MovementCount30d); // opening + relocation event (OUT tarafı)
    }

    // 8 — Duplicate signal does not inflate risk.
    [Fact]
    public async Task Duplicate_signal_does_not_inflate_risk()
    {
        var (store, _, sku, warehouse, location, _, risk) = await CreateRiskWorldAsync();

        var report = new ReportPickNotFound(store, new MasterDataQueryContract(Db.CreateMasterDataContext()), new FacilityQueryContract(Db.CreateFacilityContext()));
        var requestId = Guid.NewGuid();
        var command = new ReportPickNotFoundCommand(requestId, sku, warehouse, location, AccuracySourceType.Pick, null, null);

        await report.Handle(command, CancellationToken.None);
        await report.Handle(command, CancellationToken.None);

        var assessment = await risk.Handle(warehouse, sku, location, CancellationToken.None);

        Assert.Equal(1, assessment.NotFoundCount30d);
        Assert.Equal(1, assessment.ConsecutiveNotFound);
    }

    // 9 — Picking location context affects score (integration).
    [Fact]
    public async Task Picking_location_context_affects_score()
    {
        var (_, _, sku, warehouse, location, allowsPicking, risk) = await CreateRiskWorldAsync(allowsPicking: true);

        var assessment = await risk.Handle(warehouse, sku, location, CancellationToken.None);

        Assert.True(allowsPicking);
        Assert.Contains(assessment.Reasons, r => r.Code == "PICKING_LOCATION");
    }

    // 11 + 12 — Risk calculation does not change inventory; RED does not auto-correct.
    [Fact]
    public async Task Red_risk_does_not_change_inventory()
    {
        var (store, db, sku, warehouse, location, _, risk) = await CreateRiskWorldAsync(allowsPicking: true, openingQuantity: 25);

        await BackdateLedgerAsync(db, warehouse, sku, "400 days");

        var report = new ReportPickNotFound(store, new MasterDataQueryContract(Db.CreateMasterDataContext()), new FacilityQueryContract(Db.CreateFacilityContext()));
        await report.Handle(new ReportPickNotFoundCommand(Guid.NewGuid(), sku, warehouse, location, AccuracySourceType.Pick, null, null), CancellationToken.None);
        await report.Handle(new ReportPickNotFoundCommand(Guid.NewGuid(), sku, warehouse, location, AccuracySourceType.Pick, null, null), CancellationToken.None);

        var assessment = await risk.Handle(warehouse, sku, location, CancellationToken.None);

        Assert.Equal(RiskLevel.Red, assessment.RiskLevel);
        Assert.True(assessment.RiskScore >= 80);

        var balance = await store.GetBalanceAsync(warehouse, sku, location, InventoryStatus.Available, CancellationToken.None);
        Assert.NotNull(balance);
        Assert.Equal(25, balance!.Quantity);
        Assert.Equal(0, balance.Allocated);
    }

    // 13 — High-risk ranking orders by score descending.
    [Fact]
    public async Task Risk_listing_ranks_by_score_descending()
    {
        var (store, db, sku, warehouse, location, _, _) = await CreateRiskWorldAsync(allowsPicking: true, openingQuantity: 10);
        await BackdateLedgerAsync(db, warehouse, sku, "400 days");

        var report = new ReportPickNotFound(store, new MasterDataQueryContract(Db.CreateMasterDataContext()), new FacilityQueryContract(Db.CreateFacilityContext()));
        await report.Handle(new ReportPickNotFoundCommand(Guid.NewGuid(), sku, warehouse, location, AccuracySourceType.Pick, null, null), CancellationToken.None);
        await report.Handle(new ReportPickNotFoundCommand(Guid.NewGuid(), sku, warehouse, location, AccuracySourceType.Pick, null, null), CancellationToken.None);

        var freshSku = await Db.CreateSkuAsync();
        var freshLocation = await AddLocationAsync(warehouse);
        await using (var setupDb = Db.CreateInventoryContext())
        await using (var facilityDb = Db.CreateFacilityContext())
        await using (var masterDb = Db.CreateMasterDataContext())
        {
            var setupStore = new InventoryStore(setupDb);
            var opening = new RecordOpeningBalance(setupStore, new MasterDataQueryContract(masterDb), new FacilityQueryContract(facilityDb));
            await opening.Handle(new RecordOpeningBalanceCommand(Guid.NewGuid(), freshSku, warehouse, freshLocation, InventoryStatus.Available, 50), CancellationToken.None);
        }

        var list = new ListRiskAssessments(store, new FacilityQueryContract(Db.CreateFacilityContext()), new InventoryRiskAnalyzer(new RiskPolicyOptions()));
        var assessments = await list.Handle(warehouse, null, null, null, 50, CancellationToken.None);

        var scores = assessments.Select(a => a.RiskScore).ToList();
        Assert.Equal(scores.OrderByDescending(s => s), scores);
        Assert.Contains(assessments, a => a.SkuId == sku && a.LocationId == location && a.RiskLevel == RiskLevel.Red);
    }

    // 15 — PostgreSQL risk queries work end-to-end (consecutive chain-break rule).
    [Fact]
    public async Task Consecutive_not_found_chain_breaks_on_physical_movement()
    {
        var (store, _, sku, warehouse, location, _, risk) = await CreateRiskWorldAsync();
        var destination = await AddLocationAsync(warehouse);

        var report = new ReportPickNotFound(store, new MasterDataQueryContract(Db.CreateMasterDataContext()), new FacilityQueryContract(Db.CreateFacilityContext()));
        var relocate = new RelocateStock(store, new MasterDataQueryContract(Db.CreateMasterDataContext()), new FacilityQueryContract(Db.CreateFacilityContext()));

        await report.Handle(new ReportPickNotFoundCommand(Guid.NewGuid(), sku, warehouse, location, AccuracySourceType.Pick, null, null), CancellationToken.None);
        await relocate.Handle(new RelocateCommand(Guid.NewGuid(), sku, warehouse, location, destination, 5), CancellationToken.None);
        await report.Handle(new ReportPickNotFoundCommand(Guid.NewGuid(), sku, warehouse, location, AccuracySourceType.Pick, null, null), CancellationToken.None);
        await report.Handle(new ReportPickNotFoundCommand(Guid.NewGuid(), sku, warehouse, location, AccuracySourceType.Pick, null, null), CancellationToken.None);

        var assessment = await risk.Handle(warehouse, sku, location, CancellationToken.None);

        Assert.Equal(3, assessment.NotFoundCount30d);
        Assert.Equal(2, assessment.ConsecutiveNotFound); // ilk sinyal fiziksel hareketle zincir kırdı
        Assert.Contains(assessment.Reasons, r => r.Code == "REPEATED_NOT_FOUND");
    }

    private static async Task<Guid> AddLocationAsync(Guid warehouseId, bool allowsPicking = false)
    {
        await using var db = Db.CreateFacilityContext();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var location = Wms.Modules.Facility.Domain.Location.Create(
            warehouseId, null, $"RISK-{suffix}", "Risk Lokasyonu", Wms.Modules.Facility.Domain.LocationType.Storage, allowsPicking: allowsPicking, holdsInventory: true);
        db.Add(location);
        await db.SaveChangesAsync();
        return location.Id;
    }
}
