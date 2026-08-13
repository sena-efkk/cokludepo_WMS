using Microsoft.EntityFrameworkCore;
using Wms.Modules.Facility.Contracts;
using Wms.Modules.Facility.Infrastructure.Persistence;
using Wms.Modules.Inventory.Application;
using Wms.Modules.Inventory.Domain;
using Wms.Modules.Inventory.Infrastructure.Persistence;
using Wms.Modules.MasterData.Contracts;
using Wms.Modules.MasterData.Infrastructure.Persistence;
using Xunit;

namespace Wms.InventoryTests.Persistence;

public sealed class MovementIntegrationTests
{
    private static async Task<(
        InventoryStore Store,
        InventoryDbContext Db,
        Guid Sku,
        Guid Warehouse,
        Guid SourceLocation,
        Guid DestinationLocation,
        RelocateStock Relocate,
        ChangeInventoryStatus ChangeStatus)> CreateMovementWorldAsync(int openingQuantity = 100)
    {
        var sku = await Db.CreateSkuAsync();
        var (warehouse, sourceLocation) = await Db.CreateWarehouseWithStorageLocationAsync();

        await using (var facilityDb = Db.CreateFacilityContext())
        {
            var suffix = Guid.NewGuid().ToString("N")[..8];
            var destination = Wms.Modules.Facility.Domain.Location.Create(
                warehouse,
                null,
                $"DEST-{suffix}",
                "Hedef Lokasyon",
                Wms.Modules.Facility.Domain.LocationType.Storage,
                holdsInventory: true);
            facilityDb.Add(destination);
            await facilityDb.SaveChangesAsync();
        }

        var destinationId = await FindDestinationLocationAsync(warehouse);

        var inventoryDb = Db.CreateInventoryContext();
        var facilityDb2 = Db.CreateFacilityContext();
        var masterDb = Db.CreateMasterDataContext();
        var store = new InventoryStore(inventoryDb);
        var masterContract = new MasterDataQueryContract(masterDb);
        var facilityContract = new FacilityQueryContract(facilityDb2);
        var opening = new RecordOpeningBalance(store, masterContract, facilityContract);

        await opening.Handle(
            new RecordOpeningBalanceCommand(Guid.NewGuid(), sku, warehouse, sourceLocation, InventoryStatus.Available, openingQuantity),
            CancellationToken.None);

        var relocate = new RelocateStock(store, masterContract, facilityContract);
        var changeStatus = new ChangeInventoryStatus(store, masterContract, facilityContract);

        return (store, inventoryDb, sku, warehouse, sourceLocation, destinationId, relocate, changeStatus);
    }

    private static async Task<Guid> FindDestinationLocationAsync(Guid warehouseId)
    {
        await using var db = Db.CreateFacilityContext();
        return await db.Locations
            .Where(l => l.WarehouseId == warehouseId && l.Code.StartsWith("DEST-"))
            .OrderByDescending(l => l.CreatedAt)
            .Select(l => l.Id)
            .FirstAsync();
    }

    private static async Task<InventoryBalance?> GetBalanceAsync(
        InventoryStore store, Guid warehouse, Guid sku, Guid location, InventoryStatus status)
    {
        return await store.GetBalanceAsync(warehouse, sku, location, status, CancellationToken.None);
    }

    // 1 — Same-warehouse relocation succeeds.
    [Fact]
    public async Task Relocation_moves_stock_between_locations_in_same_warehouse()
    {
        var (store, _, sku, warehouse, source, dest, relocate, _) = await CreateMovementWorldAsync();

        var result = await relocate.Handle(
            new RelocateCommand(Guid.NewGuid(), sku, warehouse, source, dest, 40),
            CancellationToken.None);

        Assert.Equal(MovementOutcome.Performed, result.Outcome);
        Assert.Equal(60, (await GetBalanceAsync(store, warehouse, sku, source, InventoryStatus.Available))!.Quantity);
        Assert.Equal(40, (await GetBalanceAsync(store, warehouse, sku, dest, InventoryStatus.Available))!.Quantity);
    }

    // 2 — Cross-warehouse relocation rejected.
    [Fact]
    public async Task Cross_warehouse_relocation_is_rejected()
    {
        var (_, _, sku, warehouse, source, _, relocate, _) = await CreateMovementWorldAsync();
        var (otherWarehouse, _) = await Db.CreateWarehouseWithStorageLocationAsync();

        await Assert.ThrowsAsync<LocationValidationException>(() => relocate.Handle(
            new RelocateCommand(Guid.NewGuid(), sku, otherWarehouse, source, source, 1),
            CancellationToken.None));

        await Assert.ThrowsAsync<LocationValidationException>(() => relocate.Handle(
            new RelocateCommand(Guid.NewGuid(), sku, warehouse, source, Guid.NewGuid(), 1),
            CancellationToken.None));
    }

    // 3 — Inactive source location rejected.
    [Fact]
    public async Task Inactive_source_location_is_rejected()
    {
        var (_, _, sku, warehouse, source, _, relocate, _) = await CreateMovementWorldAsync();

        await using var facilityDb = Db.CreateFacilityContext();
        var location = await facilityDb.Locations.FirstAsync(l => l.Id == source);
        location.Deactivate();
        await facilityDb.SaveChangesAsync();

        await Assert.ThrowsAsync<LocationValidationException>(() => relocate.Handle(
            new RelocateCommand(Guid.NewGuid(), sku, warehouse, location.Id, location.Id, 5),
            CancellationToken.None));
    }

    // 4 — Destination in another warehouse rejected.
    [Fact]
    public async Task Destination_in_other_warehouse_is_rejected()
    {
        var (_, _, sku, warehouse, source, _, relocate, _) = await CreateMovementWorldAsync();
        var (otherWarehouse, otherLocation) = await Db.CreateWarehouseWithStorageLocationAsync();

        await Assert.ThrowsAsync<LocationValidationException>(() => relocate.Handle(
            new RelocateCommand(Guid.NewGuid(), sku, warehouse, source, otherLocation, 5),
            CancellationToken.None));

        Assert.True(otherWarehouse != warehouse);
    }

    // 5 — Insufficient quantity rejected.
    [Fact]
    public async Task Insufficient_quantity_is_rejected()
    {
        var (store, _, sku, warehouse, source, dest, relocate, _) = await CreateMovementWorldAsync(10);

        await Assert.ThrowsAsync<InsufficientInventoryException>(() => relocate.Handle(
            new RelocateCommand(Guid.NewGuid(), sku, warehouse, source, dest, 11),
            CancellationToken.None));

        Assert.Equal(10, (await GetBalanceAsync(store, warehouse, sku, source, InventoryStatus.Available))!.Quantity);
    }

    // 6 — Allocated stock cannot be moved.
    [Fact]
    public async Task Allocated_stock_cannot_be_relocated()
    {
        var (store, _, sku, warehouse, source, dest, relocate, _) = await CreateMovementWorldAsync(10);

        var reserve = new Reserve(store, new MasterDataQueryContract(Db.CreateMasterDataContext()), new FacilityQueryContract(Db.CreateFacilityContext()));
        await reserve.Handle(new ReserveCommand(Guid.NewGuid(), sku, warehouse, 7, "hold"), CancellationToken.None);

        await Assert.ThrowsAsync<InsufficientInventoryException>(() => relocate.Handle(
            new RelocateCommand(Guid.NewGuid(), sku, warehouse, source, dest, 5),
            CancellationToken.None));

        Assert.Equal(10, (await GetBalanceAsync(store, warehouse, sku, source, InventoryStatus.Available))!.Quantity);
        Assert.Equal(7, (await GetBalanceAsync(store, warehouse, sku, source, InventoryStatus.Available))!.Allocated);
    }

    // 7 — AVAILABLE → HOLD status change.
    [Fact]
    public async Task Status_change_to_hold_moves_free_stock()
    {
        var (store, _, sku, warehouse, source, _, _, changeStatus) = await CreateMovementWorldAsync(50);

        var result = await changeStatus.Handle(
            new ChangeStatusCommand(Guid.NewGuid(), sku, warehouse, source, InventoryStatus.Available, InventoryStatus.Hold, 20),
            CancellationToken.None);

        Assert.Equal(MovementOutcome.Performed, result.Outcome);
        Assert.Equal(30, (await GetBalanceAsync(store, warehouse, sku, source, InventoryStatus.Available))!.Quantity);
        Assert.Equal(20, (await GetBalanceAsync(store, warehouse, sku, source, InventoryStatus.Hold))!.Quantity);
    }

    // 8 — AVAILABLE → QUARANTINE status change.
    [Fact]
    public async Task Status_change_to_quarantine_moves_free_stock()
    {
        var (store, _, sku, warehouse, source, _, _, changeStatus) = await CreateMovementWorldAsync(50);

        await changeStatus.Handle(
            new ChangeStatusCommand(Guid.NewGuid(), sku, warehouse, source, InventoryStatus.Available, InventoryStatus.Quarantine, 15),
            CancellationToken.None);

        Assert.Equal(35, (await GetBalanceAsync(store, warehouse, sku, source, InventoryStatus.Available))!.Quantity);
        Assert.Equal(15, (await GetBalanceAsync(store, warehouse, sku, source, InventoryStatus.Quarantine))!.Quantity);
    }

    // 9 — Allocated quantity cannot be moved out of AVAILABLE via status change.
    [Fact]
    public async Task Status_change_cannot_move_allocated_stock()
    {
        var (store, _, sku, warehouse, source, _, _, changeStatus) = await CreateMovementWorldAsync(10);

        var reserve = new Reserve(store, new MasterDataQueryContract(Db.CreateMasterDataContext()), new FacilityQueryContract(Db.CreateFacilityContext()));
        await reserve.Handle(new ReserveCommand(Guid.NewGuid(), sku, warehouse, 8, "hold"), CancellationToken.None);

        await Assert.ThrowsAsync<InsufficientInventoryException>(() => changeStatus.Handle(
            new ChangeStatusCommand(Guid.NewGuid(), sku, warehouse, source, InventoryStatus.Available, InventoryStatus.Hold, 5),
            CancellationToken.None));

        Assert.Equal(10, (await GetBalanceAsync(store, warehouse, sku, source, InventoryStatus.Available))!.Quantity);
        Assert.Equal(8, (await GetBalanceAsync(store, warehouse, sku, source, InventoryStatus.Available))!.Allocated);
    }

    // 10 + 11 — Source decrement + destination increment atomic; ledger in same transaction.
    [Fact]
    public async Task Relocation_is_atomic_and_writes_correlated_ledger_entries()
    {
        var (store, _, sku, warehouse, source, dest, relocate, _) = await CreateMovementWorldAsync(100);

        var result = await relocate.Handle(
            new RelocateCommand(Guid.NewGuid(), sku, warehouse, source, dest, 30),
            CancellationToken.None);

        var ledger = await store.ListLedgerAsync(warehouse, sku, null, 20, CancellationToken.None);
        var outEntry = ledger.Single(e => e.EntryType == LedgerEntryType.RelocatedOut);
        var inEntry = ledger.Single(e => e.EntryType == LedgerEntryType.RelocatedIn);

        Assert.Equal(-30, outEntry.QuantityDelta);
        Assert.Equal(30, inEntry.QuantityDelta);
        Assert.Equal(result.MovementId, outEntry.MovementId);
        Assert.Equal(outEntry.MovementId, inEntry.MovementId);
        Assert.Equal(source, outEntry.LocationId);
        Assert.Equal(dest, inEntry.LocationId);
    }

    // 12 — Relocation does not change warehouse physical total.
    [Fact]
    public async Task Relocation_preserves_warehouse_physical_total()
    {
        var (store, _, sku, warehouse, source, dest, relocate, _) = await CreateMovementWorldAsync(100);
        var summary = new GetWarehouseSkuSummary(store);

        var before = await summary.Handle(warehouse, sku, CancellationToken.None);
        await relocate.Handle(new RelocateCommand(Guid.NewGuid(), sku, warehouse, source, dest, 45), CancellationToken.None);
        var after = await summary.Handle(warehouse, sku, CancellationToken.None);

        Assert.Equal(before.OnHand, after.OnHand);
        Assert.Equal(100, after.OnHand);
    }

    // 13 — Status change does not change warehouse physical total.
    [Fact]
    public async Task Status_change_preserves_warehouse_physical_total()
    {
        var (store, _, sku, warehouse, source, _, _, changeStatus) = await CreateMovementWorldAsync(100);
        var summary = new GetWarehouseSkuSummary(store);

        var before = await summary.Handle(warehouse, sku, CancellationToken.None);
        await changeStatus.Handle(
            new ChangeStatusCommand(Guid.NewGuid(), sku, warehouse, source, InventoryStatus.Available, InventoryStatus.Damaged, 25),
            CancellationToken.None);
        var after = await summary.Handle(warehouse, sku, CancellationToken.None);

        Assert.Equal(before.OnHand, after.OnHand);
        Assert.Equal(100, after.OnHand);
    }

    // 14 — Duplicate RequestId does not double-move.
    [Fact]
    public async Task Duplicate_request_id_does_not_double_move()
    {
        var (store, _, sku, warehouse, source, dest, relocate, _) = await CreateMovementWorldAsync(100);
        var requestId = Guid.NewGuid();
        var command = new RelocateCommand(requestId, sku, warehouse, source, dest, 20);

        var first = await relocate.Handle(command, CancellationToken.None);
        var second = await relocate.Handle(command, CancellationToken.None);

        Assert.Equal(MovementOutcome.Performed, first.Outcome);
        Assert.Equal(MovementOutcome.AlreadyRecorded, second.Outcome);
        Assert.Equal(first.MovementId, second.MovementId);

        Assert.Equal(80, (await GetBalanceAsync(store, warehouse, sku, source, InventoryStatus.Available))!.Quantity);
        Assert.Equal(20, (await GetBalanceAsync(store, warehouse, sku, dest, InventoryStatus.Available))!.Quantity);
    }

    // 15 + 16 — Concurrent over-move: only one request wins; source never negative.
    [Fact]
    public async Task Concurrent_over_move_allows_exactly_one_winner()
    {
        var sku = await Db.CreateSkuAsync();
        var (warehouse, source) = await Db.CreateWarehouseWithStorageLocationAsync();

        Guid destinationId;
        await using (var facilityDb = Db.CreateFacilityContext())
        {
            var suffix = Guid.NewGuid().ToString("N")[..8];
            var destination = Wms.Modules.Facility.Domain.Location.Create(
                warehouse, null, $"RACE-DEST-{suffix}", "Yarış Hedefi", Wms.Modules.Facility.Domain.LocationType.Storage, holdsInventory: true);
            facilityDb.Add(destination);
            await facilityDb.SaveChangesAsync();
            destinationId = destination.Id;
        }

        await using (var setupDb = Db.CreateInventoryContext())
        await using (var facilityDb = Db.CreateFacilityContext())
        await using (var masterDb = Db.CreateMasterDataContext())
        {
            var setupStore = new InventoryStore(setupDb);
            var opening = new RecordOpeningBalance(setupStore, new MasterDataQueryContract(masterDb), new FacilityQueryContract(facilityDb));
            await opening.Handle(
                new RecordOpeningBalanceCommand(Guid.NewGuid(), sku, warehouse, source, InventoryStatus.Available, 10),
                CancellationToken.None);
        }

        var results = await Task.WhenAll(
            RunRelocateAsync(sku, warehouse, source, destinationId, 7),
            RunRelocateAsync(sku, warehouse, source, destinationId, 7));

        Assert.Equal(1, results.Count(r => r.Success));

        await using var verifyDb = Db.CreateInventoryContext();
        var verifyStore = new InventoryStore(verifyDb);
        var sourceBalance = await verifyStore.GetBalanceAsync(warehouse, sku, source, InventoryStatus.Available, CancellationToken.None);
        var destBalance = await verifyStore.GetBalanceAsync(warehouse, sku, destinationId, InventoryStatus.Available, CancellationToken.None);

        Assert.Equal(3, sourceBalance!.Quantity);
        Assert.True(sourceBalance.Quantity >= 0);
        Assert.Equal(7, destBalance!.Quantity);
    }

    private static async Task<(bool Success, Exception? Error)> RunRelocateAsync(
        Guid sku, Guid warehouse, Guid source, Guid destination, int quantity)
    {
        try
        {
            await using var db = Db.CreateInventoryContext();
            await using var facilityDb = Db.CreateFacilityContext();
            await using var masterDb = Db.CreateMasterDataContext();
            var store = new InventoryStore(db);
            var relocate = new RelocateStock(store, new MasterDataQueryContract(masterDb), new FacilityQueryContract(facilityDb));
            await relocate.Handle(new RelocateCommand(Guid.NewGuid(), sku, warehouse, source, destination, quantity), CancellationToken.None);
            return (true, null);
        }
        catch (InsufficientInventoryException exception)
        {
            return (false, exception);
        }
    }

    // 17 — Destination duplicate balance is not created by concurrent moves.
    [Fact]
    public async Task Concurrent_moves_into_new_destination_do_not_create_duplicate_balances()
    {
        var sku = await Db.CreateSkuAsync();
        var (warehouse, sourceA) = await Db.CreateWarehouseWithStorageLocationAsync();
        var sourceBId = Guid.Empty;

        await using (var facilityDb = Db.CreateFacilityContext())
        {
            var suffix = Guid.NewGuid().ToString("N")[..8];
            var sourceB = Wms.Modules.Facility.Domain.Location.Create(
                warehouse, null, $"SRC-B-{suffix}", "Kaynak B", Wms.Modules.Facility.Domain.LocationType.Storage, holdsInventory: true);
            var dest = Wms.Modules.Facility.Domain.Location.Create(
                warehouse, null, $"DEST-{suffix}", "Hedef", Wms.Modules.Facility.Domain.LocationType.Storage, holdsInventory: true);
            facilityDb.AddRange(sourceB, dest);
            await facilityDb.SaveChangesAsync();
            sourceBId = sourceB.Id;
        }

        var destinationId = await FindDestinationLocationAsync(warehouse);

        await using (var setupDb = Db.CreateInventoryContext())
        await using (var facilityDb2 = Db.CreateFacilityContext())
        await using (var masterDb = Db.CreateMasterDataContext())
        {
            var setupStore = new InventoryStore(setupDb);
            var opening = new RecordOpeningBalance(setupStore, new MasterDataQueryContract(masterDb), new FacilityQueryContract(facilityDb2));
            await opening.Handle(new RecordOpeningBalanceCommand(Guid.NewGuid(), sku, warehouse, sourceA, InventoryStatus.Available, 5), CancellationToken.None);
            await opening.Handle(new RecordOpeningBalanceCommand(Guid.NewGuid(), sku, warehouse, sourceBId, InventoryStatus.Available, 5), CancellationToken.None);
        }

        await Task.WhenAll(
            RunRelocateAsync(sku, warehouse, sourceA, destinationId, 5),
            RunRelocateAsync(sku, warehouse, sourceBId, destinationId, 5));

        await using var verifyDb = Db.CreateInventoryContext();
        var verifyStore = new InventoryStore(verifyDb);
        var balances = await verifyStore.ListBalancesAsync(warehouse, sku, destinationId, includeEmpty: true, CancellationToken.None);

        var available = Assert.Single(balances);
        Assert.Equal(10, available.Quantity);
    }
}
