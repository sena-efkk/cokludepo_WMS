using Microsoft.EntityFrameworkCore;
using Npgsql;
using Wms.Modules.Facility.Contracts;
using Wms.Modules.Facility.Infrastructure.Persistence;
using Wms.Modules.Inventory.Application;
using Wms.Modules.Inventory.Domain;
using Wms.Modules.Inventory.Infrastructure.Persistence;
using Wms.Modules.MasterData.Contracts;
using Wms.Modules.MasterData.Infrastructure.Persistence;
using Xunit;

namespace Wms.InventoryTests.Persistence;

public sealed class InventoryIntegrationTests
{
    private static async Task<(InventoryStore Store, InventoryDbContext Db, Guid Sku, Guid Warehouse, Guid Location, RecordOpeningBalance Opening, Reserve Reserve, ReleaseReservation Release, ConsumeReservation Consume, GetWarehouseSkuSummary Summary)> CreateWorldAsync(
        int locationCount = 1,
        Dictionary<int, int>? quantities = null)
    {
        var sku = await Db.CreateSkuAsync();
        var (warehouse, location) = await Db.CreateWarehouseWithStorageLocationAsync();

        var inventoryDb = Db.CreateInventoryContext();
        var facilityDb = Db.CreateFacilityContext();
        var masterDb = Db.CreateMasterDataContext();
        var store = new InventoryStore(inventoryDb);
        var opening = new RecordOpeningBalance(store, new MasterDataQueryContract(masterDb), new FacilityQueryContract(facilityDb));
        var reserve = new Reserve(store, new MasterDataQueryContract(masterDb), new FacilityQueryContract(facilityDb));
        var release = new ReleaseReservation(store);
        var consume = new ConsumeReservation(store);
        var summary = new GetWarehouseSkuSummary(store);

        var qty = quantities is null ? 100 : quantities.GetValueOrDefault(0, 100);
        var openingResult = await opening.Handle(
            new RecordOpeningBalanceCommand(Guid.NewGuid(), sku, warehouse, location, InventoryStatus.Available, qty),
            CancellationToken.None);
        Assert.Equal(OpeningBalanceOutcome.Recorded, openingResult.Outcome);

        if (locationCount > 1)
        {
            for (var i = 1; i < locationCount; i++)
            {
                var extraLocationId = await AddLocationAsync(facilityDb, warehouse, i);
                var extraQty = quantities?.GetValueOrDefault(i, 0) ?? 0;
                if (extraQty > 0)
                {
                    await opening.Handle(
                        new RecordOpeningBalanceCommand(Guid.NewGuid(), sku, warehouse, extraLocationId, InventoryStatus.Available, extraQty),
                        CancellationToken.None);
                }
            }
        }

        return (store, inventoryDb, sku, warehouse, location, opening, reserve, release, consume, summary);
    }

    private static async Task<Guid> AddLocationAsync(FacilityDbContext db, Guid warehouseId, int index)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var location = Wms.Modules.Facility.Domain.Location.Create(
            warehouseId,
            null,
            $"STORE-{index}-{suffix}",
            $"Ek Lokasyon {index}",
            Wms.Modules.Facility.Domain.LocationType.Storage,
            holdsInventory: true);
        db.Add(location);
        await db.SaveChangesAsync();
        return location.Id;
    }

    // 1 — Opening balance creates balance + ledger atomically.
    [Fact]
    public async Task Opening_balance_creates_balance_and_ledger_atomically()
    {
        var (store, db, sku, warehouse, location, _, _, _, _, _) = await CreateWorldAsync();

        var balance = await store.GetBalanceAsync(warehouse, sku, location, InventoryStatus.Available, CancellationToken.None);
        Assert.NotNull(balance);
        Assert.Equal(100, balance!.Quantity);
        Assert.Equal(0, balance.Allocated);

        var ledger = await store.ListLedgerAsync(warehouse, sku, location, 10, CancellationToken.None);
        var openingEntry = Assert.Single(ledger);
        Assert.Equal(LedgerEntryType.OpeningBalance, openingEntry.EntryType);
        Assert.Equal(100, openingEntry.QuantityDelta);
    }

    // 2 — Duplicate opening balance request does not double-add.
    [Fact]
    public async Task Duplicate_opening_balance_request_does_not_double_add()
    {
        var (store, _, sku, warehouse, location, opening, _, _, _, _) = await CreateWorldAsync(quantities: new Dictionary<int, int> { [0] = 0 });
        var requestId = Guid.NewGuid();
        var command = new RecordOpeningBalanceCommand(requestId, sku, warehouse, location, InventoryStatus.Available, 100);

        var first = await opening.Handle(command, CancellationToken.None);
        var second = await opening.Handle(command, CancellationToken.None);

        Assert.Equal(OpeningBalanceOutcome.Recorded, first.Outcome);
        Assert.Equal(OpeningBalanceOutcome.AlreadyRecorded, second.Outcome);
        var balance = await store.GetBalanceAsync(warehouse, sku, location, InventoryStatus.Available, CancellationToken.None);
        Assert.Equal(100, balance!.Quantity);
    }

    // 3 — Negative quantity rejected by DB.
    [Fact]
    public async Task Negative_quantity_is_rejected_by_database()
    {
        var sku = await Db.CreateSkuAsync();
        var (warehouse, location) = await Db.CreateWarehouseWithStorageLocationAsync();
        await using var db = Db.CreateInventoryContext();

        var exception = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO inventory.inventory_balance (id, sku_id, warehouse_id, location_id, status, quantity, allocated, created_at, updated_at)
            VALUES ({0}, {1}, {2}, {3}, 'AVAILABLE', -1, 0, now(), now())
            """,
            Guid.NewGuid(),
            sku,
            warehouse,
            location));

        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
    }

    // 4 — Allocated > Quantity impossible.
    [Fact]
    public async Task Allocated_exceeding_quantity_is_rejected_by_database()
    {
        var (_, db, sku, warehouse, location, _, _, _, _, _) = await CreateWorldAsync(quantities: new Dictionary<int, int> { [0] = 5 });

        db.ChangeTracker.Clear();

        var exception = await Assert.ThrowsAsync<PostgresException>(() => db.InventoryBalances
            .Where(b => b.WarehouseId == warehouse && b.SkuId == sku && b.LocationId == location)
            .ExecuteUpdateAsync(setters => setters.SetProperty(b => b.Allocated, 99)));

        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
    }

    // 5 — HOLD / QUARANTINE / DAMAGED cannot be allocated.
    [Theory]
    [InlineData(InventoryStatus.Hold)]
    [InlineData(InventoryStatus.Quarantine)]
    [InlineData(InventoryStatus.Damaged)]
    public async Task Non_available_status_cannot_be_allocated(InventoryStatus status)
    {
        var (_, _, sku, warehouse, location, opening, reserve, _, _, _) = await CreateWorldAsync(quantities: new Dictionary<int, int> { [0] = 0 });
        await opening.Handle(
            new RecordOpeningBalanceCommand(Guid.NewGuid(), sku, warehouse, location, status, 50),
            CancellationToken.None);

        await Assert.ThrowsAsync<InsufficientInventoryException>(() => reserve.Handle(
            new ReserveCommand(Guid.NewGuid(), sku, warehouse, 10, "test"),
            CancellationToken.None));
    }

    // 5b — DB rejects allocated>0 on non-AVAILABLE status.
    [Fact]
    public async Task Database_rejects_allocated_on_non_available_status()
    {
        var (_, db, sku, warehouse, location, _, _, _, _, _) = await CreateWorldAsync();
        db.ChangeTracker.Clear();

        var hold = InventoryBalance.Create(sku, warehouse, location, InventoryStatus.Hold, 10);
        db.Add(hold);
        await db.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<PostgresException>(() => db.InventoryBalances
            .Where(b => b.Id == hold.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(b => b.Allocated, 3)));

        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
    }

    // 6 — Available computed correctly (physical vs ATP separation).
    [Fact]
    public async Task Summary_separates_physical_total_from_available()
    {
        var (_, _, sku, warehouse, location, opening, reserve, _, _, summary) = await CreateWorldAsync(quantities: new Dictionary<int, int> { [0] = 100 });
        await opening.Handle(
            new RecordOpeningBalanceCommand(Guid.NewGuid(), sku, warehouse, location, InventoryStatus.Hold, 10),
            CancellationToken.None);
        await opening.Handle(
            new RecordOpeningBalanceCommand(Guid.NewGuid(), sku, warehouse, location, InventoryStatus.Quarantine, 5),
            CancellationToken.None);
        await reserve.Handle(new ReserveCommand(Guid.NewGuid(), sku, warehouse, 20, "test"), CancellationToken.None);

        var result = await summary.Handle(warehouse, sku, CancellationToken.None);

        Assert.Equal(115, result.OnHand);      // 100 + 10 + 5 fiziksel
        Assert.Equal(20, result.Allocated);
        Assert.Equal(80, result.Available);    // 100 - 20; HOLD/QUARANTINE ATP'ye girmez
        Assert.Equal(3, result.ByStatus.Count);
    }

    // 7 — Single-location reservation.
    [Fact]
    public async Task Single_location_reservation()
    {
        var (_, _, sku, warehouse, location, _, reserve, _, _, _) = await CreateWorldAsync();

        var reservation = await reserve.Handle(new ReserveCommand(Guid.NewGuid(), sku, warehouse, 30, "order"), CancellationToken.None);

        Assert.Equal(ReservationStatus.Allocated, reservation.Status);
        Assert.Equal(30, reservation.RequestedQuantity);
        var line = Assert.Single(reservation.Lines);
        Assert.Equal(location, line.LocationId);
        Assert.Equal(30, line.Quantity);
    }

    // 8 — Multi-location reservation.
    [Fact]
    public async Task Multi_location_reservation_splits_across_bins()
    {
        var (_, _, sku, warehouse, _, _, reserve, _, _, _) = await CreateWorldAsync(
            locationCount: 2,
            quantities: new Dictionary<int, int> { [0] = 3, [1] = 4 });

        var reservation = await reserve.Handle(new ReserveCommand(Guid.NewGuid(), sku, warehouse, 5, "order"), CancellationToken.None);

        Assert.Equal(5, reservation.RequestedQuantity);
        Assert.Equal(2, reservation.Lines.Count);
        Assert.Equal(5, reservation.Lines.Sum(l => l.Quantity));
    }

    // 9 — Insufficient stock leaves no partial reservation.
    [Fact]
    public async Task Insufficient_stock_leaves_no_partial_reservation()
    {
        var (store, db, sku, warehouse, _, _, reserve, _, _, _) = await CreateWorldAsync(
            locationCount: 2,
            quantities: new Dictionary<int, int> { [0] = 3, [1] = 4 });

        await Assert.ThrowsAsync<InsufficientInventoryException>(() => reserve.Handle(
            new ReserveCommand(Guid.NewGuid(), sku, warehouse, 9, "order"),
            CancellationToken.None));

        var balances = await store.ListBalancesAsync(warehouse, sku, null, includeEmpty: true, CancellationToken.None);
        Assert.All(balances, b => Assert.Equal(0, b.Allocated));
        Assert.False(await db.InventoryReservations.AnyAsync(r => r.WarehouseId == warehouse && r.SkuId == sku));
        Assert.False(await db.InventoryLedgerEntries.AnyAsync(e => e.WarehouseId == warehouse && e.SkuId == sku && e.EntryType == LedgerEntryType.Reserved));
    }

    // 10 — CONCURRENT LAST-STOCK: yalnız biri kazanır.
    [Fact]
    public async Task Concurrent_last_stock_race_allows_exactly_one_winner()
    {
        var sku = await Db.CreateSkuAsync();
        var (warehouse, location) = await Db.CreateWarehouseWithStorageLocationAsync();

        await using (var setupDb = Db.CreateInventoryContext())
        await using (var facilityDb = Db.CreateFacilityContext())
        await using (var masterDb = Db.CreateMasterDataContext())
        {
            var setupStore = new InventoryStore(setupDb);
            var opening = new RecordOpeningBalance(setupStore, new MasterDataQueryContract(masterDb), new FacilityQueryContract(facilityDb));
            await opening.Handle(
                new RecordOpeningBalanceCommand(Guid.NewGuid(), sku, warehouse, location, InventoryStatus.Available, 1),
                CancellationToken.None);
        }

        var results = await Task.WhenAll(
            RunReserveAsync(sku, warehouse),
            RunReserveAsync(sku, warehouse));

        var winners = results.Count(r => r.Success);
        Assert.Equal(1, winners);

        await using var verifyDb = Db.CreateInventoryContext();
        var verifyStore = new InventoryStore(verifyDb);
        var balance = await verifyStore.GetBalanceAsync(warehouse, sku, location, InventoryStatus.Available, CancellationToken.None);
        Assert.Equal(1, balance!.Quantity);
        Assert.Equal(1, balance.Allocated);
        Assert.Equal(0, balance.Available);
    }

    private static async Task<(bool Success, Exception? Error)> RunReserveAsync(Guid sku, Guid warehouse)
    {
        try
        {
            await using var db = Db.CreateInventoryContext();
            await using var facilityDb = Db.CreateFacilityContext();
            await using var masterDb = Db.CreateMasterDataContext();
            var store = new InventoryStore(db);
            var reserve = new Reserve(store, new MasterDataQueryContract(masterDb), new FacilityQueryContract(facilityDb));
            await reserve.Handle(new ReserveCommand(Guid.NewGuid(), sku, warehouse, 1, "race"), CancellationToken.None);
            return (true, null);
        }
        catch (InsufficientInventoryException exception)
        {
            return (false, exception);
        }
    }

    // 11 — Release opens allocated stock.
    [Fact]
    public async Task Release_opens_allocated_stock()
    {
        var (store, _, sku, warehouse, location, _, reserve, release, _, _) = await CreateWorldAsync();
        var reservation = await reserve.Handle(new ReserveCommand(Guid.NewGuid(), sku, warehouse, 40, "order"), CancellationToken.None);

        await release.Handle(reservation.Id, CancellationToken.None);

        var balance = await store.GetBalanceAsync(warehouse, sku, location, InventoryStatus.Available, CancellationToken.None);
        Assert.Equal(100, balance!.Quantity);
        Assert.Equal(0, balance.Allocated);

        var ledger = await store.ListLedgerAsync(warehouse, sku, location, 10, CancellationToken.None);
        Assert.Contains(ledger, e => e.EntryType == LedgerEntryType.ReservationReleased && e.AllocatedDelta == -40);
    }

    // 12 — Duplicate release does not double-decrement.
    [Fact]
    public async Task Duplicate_release_does_not_double_decrement()
    {
        var (store, _, sku, warehouse, location, _, reserve, release, _, _) = await CreateWorldAsync();
        var reservation = await reserve.Handle(new ReserveCommand(Guid.NewGuid(), sku, warehouse, 40, "order"), CancellationToken.None);

        await release.Handle(reservation.Id, CancellationToken.None);
        await release.Handle(reservation.Id, CancellationToken.None);

        var balance = await store.GetBalanceAsync(warehouse, sku, location, InventoryStatus.Available, CancellationToken.None);
        Assert.Equal(0, balance!.Allocated);
        var releaseEntries = (await store.ListLedgerAsync(warehouse, sku, location, 20, CancellationToken.None))
            .Count(e => e.EntryType == LedgerEntryType.ReservationReleased);
        Assert.Equal(1, releaseEntries);
    }

    // 13 — Consume reduces quantity and allocated.
    [Fact]
    public async Task Consume_reduces_quantity_and_allocated()
    {
        var (store, _, sku, warehouse, location, _, reserve, _, consume, _) = await CreateWorldAsync();
        var reservation = await reserve.Handle(new ReserveCommand(Guid.NewGuid(), sku, warehouse, 25, "order"), CancellationToken.None);

        await consume.Handle(reservation.Id, CancellationToken.None);

        var balance = await store.GetBalanceAsync(warehouse, sku, location, InventoryStatus.Available, CancellationToken.None);
        Assert.Equal(75, balance!.Quantity);
        Assert.Equal(0, balance.Allocated);

        var ledger = await store.ListLedgerAsync(warehouse, sku, location, 10, CancellationToken.None);
        Assert.Contains(ledger, e => e.EntryType == LedgerEntryType.ReservationConsumed && e.QuantityDelta == -25 && e.AllocatedDelta == -25);
    }

    // 14 — Duplicate consume is a no-op.
    [Fact]
    public async Task Duplicate_consume_is_noop()
    {
        var (store, _, sku, warehouse, location, _, reserve, _, consume, _) = await CreateWorldAsync();
        var reservation = await reserve.Handle(new ReserveCommand(Guid.NewGuid(), sku, warehouse, 25, "order"), CancellationToken.None);

        await consume.Handle(reservation.Id, CancellationToken.None);
        await consume.Handle(reservation.Id, CancellationToken.None);

        var balance = await store.GetBalanceAsync(warehouse, sku, location, InventoryStatus.Available, CancellationToken.None);
        Assert.Equal(75, balance!.Quantity);
        var consumeEntries = (await store.ListLedgerAsync(warehouse, sku, location, 20, CancellationToken.None))
            .Count(e => e.EntryType == LedgerEntryType.ReservationConsumed);
        Assert.Equal(1, consumeEntries);
    }

    // 15 — Balance mutation + ledger are atomic (failed reserve leaves nothing behind).
    [Fact]
    public async Task Failed_reservation_rolls_back_operation_and_leaves_no_ledger()
    {
        var (_, db, sku, warehouse, _, _, reserve, _, _, _) = await CreateWorldAsync(quantities: new Dictionary<int, int> { [0] = 2 });
        var requestId = Guid.NewGuid();

        await Assert.ThrowsAsync<InsufficientInventoryException>(() => reserve.Handle(
            new ReserveCommand(requestId, sku, warehouse, 5, "order"),
            CancellationToken.None));

        Assert.False(await db.Set<InventoryOperation>().AnyAsync(o => o.RequestId == requestId));
        Assert.False(await db.InventoryLedgerEntries.AnyAsync(e => e.WarehouseId == warehouse && e.SkuId == sku && e.EntryType == LedgerEntryType.Reserved));
    }

    // 16 — inventory schema contains expected tables.
    [Fact]
    public async Task Inventory_schema_contains_expected_tables()
    {
        await using var db = Db.CreateInventoryContext();
        await db.Database.MigrateAsync();

        var expected = new[]
        {
            "inventory_balance",
            "inventory_reservation",
            "inventory_reservation_line",
            "inventory_ledger",
            "inventory_operation",
        };

        await using var command = db.Database.GetDbConnection();
        await command.OpenAsync();
        await using var query = command.CreateCommand();
        query.CommandText = "SELECT table_name FROM information_schema.tables WHERE table_schema = 'inventory'";
        var actual = new List<string>();
        await using var reader = await query.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            actual.Add(reader.GetString(0));
        }

        Assert.All(expected, table => Assert.Contains(table, actual));
    }

    // 17 — No cross-module DB foreign keys from inventory schema.
    [Fact]
    public async Task Inventory_schema_has_no_cross_module_foreign_keys()
    {
        await using var db = Db.CreateInventoryContext();

        await using var command = db.Database.GetDbConnection();
        await command.OpenAsync();
        await using var query = command.CreateCommand();
        query.CommandText = """
            SELECT COUNT(*)
            FROM information_schema.table_constraints tc
            WHERE tc.constraint_schema = 'inventory'
              AND tc.constraint_type = 'FOREIGN KEY'
              AND EXISTS (
                  SELECT 1
                  FROM information_schema.constraint_column_usage ccu
                  WHERE ccu.constraint_name = tc.constraint_name
                    AND ccu.table_schema <> 'inventory'
              )
            """;
        var count = Convert.ToInt32(await query.ExecuteScalarAsync());

        Assert.Equal(0, count);
    }
}
