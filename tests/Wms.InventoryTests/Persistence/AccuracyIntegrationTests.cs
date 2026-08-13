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

public sealed class AccuracyIntegrationTests
{
    private static async Task<(
        InventoryStore Store,
        InventoryDbContext Db,
        Guid Sku,
        Guid Warehouse,
        Guid Location,
        ReportPickNotFound Report)> CreateAccuracyWorldAsync(int openingQuantity = 100)
    {
        var sku = await Db.CreateSkuAsync();
        var (warehouse, location) = await Db.CreateWarehouseWithStorageLocationAsync();

        var inventoryDb = Db.CreateInventoryContext();
        var facilityDb = Db.CreateFacilityContext();
        var masterDb = Db.CreateMasterDataContext();
        var store = new InventoryStore(inventoryDb);
        var masterContract = new MasterDataQueryContract(masterDb);
        var facilityContract = new FacilityQueryContract(facilityDb);

        if (openingQuantity > 0)
        {
            var opening = new RecordOpeningBalance(store, masterContract, facilityContract);
            await opening.Handle(
                new RecordOpeningBalanceCommand(Guid.NewGuid(), sku, warehouse, location, InventoryStatus.Available, openingQuantity),
                CancellationToken.None);
        }

        var report = new ReportPickNotFound(store, masterContract, facilityContract);
        return (store, inventoryDb, sku, warehouse, location, report);
    }

    private static ReportPickNotFoundCommand NewCommand(Guid sku, Guid warehouse, Guid location, Guid? requestId = null, DateTime? occurredAt = null) =>
        new(
            requestId ?? Guid.NewGuid(),
            sku,
            warehouse,
            location,
            AccuracySourceType.Pick,
            null,
            occurredAt);

    // 1 — Valid PickNotFound signal recorded.
    [Fact]
    public async Task Valid_pick_not_found_signal_is_recorded()
    {
        var (store, db, sku, warehouse, location, report) = await CreateAccuracyWorldAsync(50);

        var result = await report.Handle(NewCommand(sku, warehouse, location), CancellationToken.None);

        Assert.Equal(SignalOutcome.Recorded, result.Outcome);
        var signals = await store.ListAccuracySignalsAsync(null, sku, location, null, null, null, 10, CancellationToken.None);
        var signal = Assert.Single(signals);
        Assert.Equal(AccuracySignalType.PickNotFound, signal.SignalType);
        Assert.Equal(AccuracySourceType.Pick, signal.SourceType);
        Assert.Equal(50, signal.SystemQuantityAtSignal);
    }

    // 2 — Duplicate RequestId does not create a second signal.
    [Fact]
    public async Task Duplicate_request_id_does_not_create_second_signal()
    {
        var (store, db, sku, warehouse, location, report) = await CreateAccuracyWorldAsync();
        var requestId = Guid.NewGuid();
        var command = NewCommand(sku, warehouse, location, requestId);

        var first = await report.Handle(command, CancellationToken.None);
        var second = await report.Handle(command, CancellationToken.None);

        Assert.Equal(SignalOutcome.Recorded, first.Outcome);
        Assert.Equal(SignalOutcome.AlreadyRecorded, second.Outcome);
        Assert.Equal(first.SignalId, second.SignalId);

        var count = await db.InventoryAccuracySignals.CountAsync(s => s.RequestId == requestId);
        Assert.Equal(1, count);
    }

    // 3 — Signal is append-only (immutable entity).
    [Fact]
    public void Signal_entity_exposes_no_public_setters()
    {
        var publicSetters = typeof(InventoryAccuracySignal)
            .GetProperties()
            .Where(p => p.SetMethod is { IsPublic: true })
            .Select(p => p.Name)
            .ToList();

        Assert.Empty(publicSetters);
    }

    // 4 — Identity validation (SKU / warehouse / location).
    [Fact]
    public async Task Unknown_sku_is_rejected()
    {
        var (_, _, _, warehouse, location, report) = await CreateAccuracyWorldAsync();

        await Assert.ThrowsAsync<SkuValidationException>(() => report.Handle(
            NewCommand(Guid.NewGuid(), warehouse, location),
            CancellationToken.None));
    }

    [Fact]
    public async Task Unknown_location_is_rejected()
    {
        var (_, _, sku, warehouse, _, report) = await CreateAccuracyWorldAsync();

        await Assert.ThrowsAsync<LocationValidationException>(() => report.Handle(
            NewCommand(sku, warehouse, Guid.NewGuid()),
            CancellationToken.None));
    }

    // 5 — Location of another warehouse is rejected.
    [Fact]
    public async Task Location_of_other_warehouse_is_rejected()
    {
        var (_, _, sku, warehouse, _, report) = await CreateAccuracyWorldAsync();
        var (otherWarehouse, otherLocation) = await Db.CreateWarehouseWithStorageLocationAsync();

        await Assert.ThrowsAsync<LocationValidationException>(() => report.Handle(
            NewCommand(sku, warehouse, otherLocation),
            CancellationToken.None));

        Assert.NotEqual(warehouse, otherWarehouse);
    }

    // 6 — Snapshot records the system expectation at signal time.
    [Fact]
    public async Task Snapshot_records_system_expectation_at_signal_time()
    {
        var (store, _, sku, warehouse, location, report) = await CreateAccuracyWorldAsync(100);

        var reserve = new Reserve(store, new MasterDataQueryContract(Db.CreateMasterDataContext()), new FacilityQueryContract(Db.CreateFacilityContext()));
        await reserve.Handle(new ReserveCommand(Guid.NewGuid(), sku, warehouse, 20, "order"), CancellationToken.None);

        var result = await report.Handle(NewCommand(sku, warehouse, location), CancellationToken.None);

        var signals = await store.ListAccuracySignalsAsync(null, sku, location, null, null, null, 10, CancellationToken.None);
        var signal = Assert.Single(signals);
        Assert.Equal(100, signal.SystemQuantityAtSignal);
        Assert.Equal(20, signal.AllocatedAtSignal);
        Assert.Equal(80, signal.AvailableAtSignal);
        Assert.Equal(InventoryStatus.Available, signal.StatusAtSignal);
    }

    // 7 — Later balance changes do not affect the historical snapshot.
    [Fact]
    public async Task Historical_snapshot_is_not_affected_by_later_changes()
    {
        var (store, _, sku, warehouse, location, report) = await CreateAccuracyWorldAsync(100);

        var signalId = (await report.Handle(NewCommand(sku, warehouse, location), CancellationToken.None)).SignalId;

        var reserve = new Reserve(store, new MasterDataQueryContract(Db.CreateMasterDataContext()), new FacilityQueryContract(Db.CreateFacilityContext()));
        await reserve.Handle(new ReserveCommand(Guid.NewGuid(), sku, warehouse, 40, "order"), CancellationToken.None);

        var signals = await store.ListAccuracySignalsAsync(null, sku, location, null, null, null, 10, CancellationToken.None);
        var signal = Assert.Single(signals);
        Assert.Equal(signalId, signal.Id);
        Assert.Equal(100, signal.SystemQuantityAtSignal);
        Assert.Equal(0, signal.AllocatedAtSignal);
        Assert.Equal(100, signal.AvailableAtSignal);
    }

    // 8 — Signal survives later SKU/Location deactivation.
    [Fact]
    public async Task Signal_survives_later_sku_and_location_deactivation()
    {
        var (store, _, sku, warehouse, location, report) = await CreateAccuracyWorldAsync();

        var signalId = (await report.Handle(NewCommand(sku, warehouse, location), CancellationToken.None)).SignalId;

        await using (var masterDb = Db.CreateMasterDataContext())
        {
            var skuEntity = await masterDb.Skus.FirstAsync(s => s.Id == sku);
            skuEntity.Deactivate();
            await masterDb.SaveChangesAsync();
        }

        await using (var facilityDb = Db.CreateFacilityContext())
        {
            var locationEntity = await facilityDb.Locations.FirstAsync(l => l.Id == location);
            locationEntity.Deactivate();
            await facilityDb.SaveChangesAsync();
        }

        var signals = await store.ListAccuracySignalsAsync(null, sku, location, null, null, null, 10, CancellationToken.None);
        var signal = Assert.Single(signals);
        Assert.Equal(signalId, signal.Id);
        Assert.Equal(AccuracySignalType.PickNotFound, signal.SignalType);
    }

    // 9 — Date-range query works.
    [Fact]
    public async Task Date_range_query_filters_signals()
    {
        var (store, _, sku, warehouse, location, report) = await CreateAccuracyWorldAsync();
        var oldOccurredAt = DateTime.UtcNow.AddDays(-10);
        var recentOccurredAt = DateTime.UtcNow;

        await report.Handle(NewCommand(sku, warehouse, location, occurredAt: oldOccurredAt), CancellationToken.None);
        await report.Handle(NewCommand(sku, warehouse, location, occurredAt: recentOccurredAt), CancellationToken.None);

        var recent = await store.ListAccuracySignalsAsync(
            null, sku, location, null, DateTime.UtcNow.AddDays(-1), null, 10, CancellationToken.None);

        var signal = Assert.Single(recent);
        Assert.True(signal.OccurredAt >= DateTime.UtcNow.AddDays(-1));
    }

    // 10 — SKU+Location NotFound history query.
    [Fact]
    public async Task Sku_location_not_found_history_is_queryable()
    {
        var (store, _, sku, warehouse, location, report) = await CreateAccuracyWorldAsync();
        var otherLocation = await AddExtraLocationAsync(warehouse);

        await report.Handle(NewCommand(sku, warehouse, location), CancellationToken.None);
        await report.Handle(NewCommand(sku, warehouse, location), CancellationToken.None);
        await report.Handle(NewCommand(sku, warehouse, otherLocation), CancellationToken.None);

        var query = new GetSignalsForSkuLocation(store);
        var history = await query.Handle(warehouse, sku, location, AccuracySignalType.PickNotFound, null, null, CancellationToken.None);

        Assert.Equal(2, history.Count);
        Assert.All(history, s => Assert.Equal(location, s.LocationId));
    }

    private static async Task<Guid> AddExtraLocationAsync(Guid warehouseId)
    {
        await using var db = Db.CreateFacilityContext();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var location = Wms.Modules.Facility.Domain.Location.Create(
            warehouseId, null, $"ACC-EXT-{suffix}", "Accuracy Ek Lokasyon", Wms.Modules.Facility.Domain.LocationType.Storage, holdsInventory: true);
        db.Add(location);
        await db.SaveChangesAsync();
        return location.Id;
    }

    // 11 — Real PostgreSQL persistence: table exists in inventory schema.
    [Fact]
    public async Task Accuracy_signal_table_exists_in_inventory_schema()
    {
        await using var db = Db.CreateInventoryContext();

        await using var command = db.Database.GetDbConnection();
        await command.OpenAsync();
        await using var query = command.CreateCommand();
        query.CommandText = "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'inventory' AND table_name = 'inventory_accuracy_signal'";
        var count = Convert.ToInt32(await query.ExecuteScalarAsync());

        Assert.Equal(1, count);
    }
}

public sealed class AccuracyDomainTests
{
    [Fact]
    public void CreatePickNotFound_rejects_empty_request_id()
    {
        Assert.Throws<ArgumentException>(() => InventoryAccuracySignal.CreatePickNotFound(
            Guid.Empty, AccuracySourceType.Pick, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, DateTime.UtcNow, 0, 0, 0, InventoryStatus.Available));
    }

    [Fact]
    public void CreatePickNotFound_rejects_negative_snapshot_quantities()
    {
        Assert.Throws<ArgumentException>(() => InventoryAccuracySignal.CreatePickNotFound(
            Guid.NewGuid(), AccuracySourceType.Pick, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, DateTime.UtcNow, -1, 0, 0, InventoryStatus.Available));
    }

    [Fact]
    public void CreatePickNotFound_defaults_occurred_at_when_not_provided()
    {
        var signal = InventoryAccuracySignal.CreatePickNotFound(
            Guid.NewGuid(), AccuracySourceType.Pick, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, default, 5, 0, 5, InventoryStatus.Available);

        Assert.True(signal.OccurredAt > DateTime.UtcNow.AddMinutes(-5));
        Assert.Equal(AccuracySignalType.PickNotFound, signal.SignalType);
    }
}
