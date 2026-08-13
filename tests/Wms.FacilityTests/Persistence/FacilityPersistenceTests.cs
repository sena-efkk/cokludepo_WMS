using Microsoft.EntityFrameworkCore;
using Wms.Modules.Facility.Application;
using Wms.Modules.Facility.Application.Seed;
using Wms.Modules.Facility.Domain;
using Wms.Modules.Facility.Infrastructure.Persistence;
using Xunit;

namespace Wms.FacilityTests.Persistence;

public sealed class FacilityPersistenceTests
{
    private static FacilityDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<FacilityDbContext>()
            .UseNpgsql(TestConnection.ResolveOrFail(), npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", "facility"))
            .UseSnakeCaseNamingConvention()
            .Options;
        return new FacilityDbContext(options);
    }

    private static string UniqueSuffix() => Guid.NewGuid().ToString("N")[..8];

    [Fact]
    public async Task Migrations_apply_and_facility_schema_contains_expected_tables()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();

        var expected = new[] { "warehouse", "location" };
        await using var command = db.Database.GetDbConnection();
        await command.OpenAsync();
        await using var query = command.CreateCommand();
        query.CommandText = "SELECT table_name FROM information_schema.tables WHERE table_schema = 'facility'";
        var actual = new List<string>();
        await using var reader = await query.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            actual.Add(reader.GetString(0));
        }

        Assert.All(expected, table => Assert.Contains(table, actual));
    }

    [Fact]
    public async Task Unique_constraint_on_warehouse_code_is_enforced()
    {
        await using var db = CreateContext();
        var code = $"W-{UniqueSuffix()}";
        db.Add(Warehouse.Create(code, "Depo 1"));
        db.Add(Warehouse.Create(code, "Depo 2"));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Unique_constraint_on_warehouse_and_location_code_is_enforced()
    {
        await using var db = CreateContext();
        var warehouse = Warehouse.Create($"W-{UniqueSuffix()}", "Depo");
        db.Add(warehouse);
        db.Add(Location.Create(warehouse.Id, null, "A01", "Koridor 1", LocationType.Aisle));
        db.Add(Location.Create(warehouse.Id, null, "A01", "Koridor 2", LocationType.Aisle));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Module_local_foreign_keys_are_enforced()
    {
        await using var db = CreateContext();
        var location = Location.Create(Guid.NewGuid(), null, "ORPHAN", "Orphan", LocationType.Bin);

        db.Add(location);

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Location_tree_persists_and_reads_back()
    {
        await using var db = CreateContext();
        var warehouse = Warehouse.Create($"T-{UniqueSuffix()}", "Tree Depo");
        var zone = Location.Create(warehouse.Id, null, "ZONE-A", "Bölge A", LocationType.Zone, holdsInventory: true);
        var bin = Location.Create(warehouse.Id, zone.Id, "ZONE-A-B01", "Göz 1", LocationType.Bin, holdsInventory: true);

        db.Add(warehouse);
        db.Add(zone);
        db.Add(bin);
        await db.SaveChangesAsync();

        await using var fresh = CreateContext();
        var store = new FacilityStore(fresh);
        var tree = await new GetLocationTree(store).Handle(warehouse.Id, includeInactive: true, CancellationToken.None);

        var root = Assert.Single(tree);
        Assert.Equal("ZONE-A", root.Code);
        Assert.Equal(LocationType.Zone.ToString(), root.Type);
        var child = Assert.Single(root.Children);
        Assert.Equal("ZONE-A-B01", child.Code);
        Assert.True(child.IsActive);
    }

    [Fact]
    public async Task Synthetic_seed_is_idempotent_against_database()
    {
        await using var db = CreateContext();
        var store = new FacilityStore(db);
        var seed = new SeedDemoFacilities(store);
        var plans = SyntheticFacilityFactory.CreatePlans();

        await seed.Handle(plans, CancellationToken.None);
        var second = await seed.Handle(plans, CancellationToken.None);

        Assert.Equal(0, second.WarehousesCreated);
        Assert.Equal(0, second.LocationsCreated);
        Assert.Equal(plans.Sum(p => p.Locations.Count), second.Skipped);
    }

    [Fact]
    public async Task Synthetic_seed_produces_distinct_layouts_per_warehouse()
    {
        await using var db = CreateContext();
        var store = new FacilityStore(db);
        var seed = new SeedDemoFacilities(store);

        await seed.Handle(SyntheticFacilityFactory.CreatePlans(), CancellationToken.None);

        var bursa = await store.GetWarehouseByCodeAsync("BURSA-01", CancellationToken.None);
        var istanbul = await store.GetWarehouseByCodeAsync("IST-01", CancellationToken.None);
        Assert.NotNull(bursa);
        Assert.NotNull(istanbul);

        var bursaTree = await new GetLocationTree(store).Handle(bursa!.Id, includeInactive: false, CancellationToken.None);
        var istanbulTree = await new GetLocationTree(store).Handle(istanbul!.Id, includeInactive: false, CancellationToken.None);

        var bursaCodes = bursaTree.Select(n => n.Code).ToHashSet();
        var istanbulCodes = istanbulTree.Select(n => n.Code).ToHashSet();

        Assert.Contains("PICKING", bursaCodes);
        Assert.Contains("RECEIVING", bursaCodes);
        Assert.DoesNotContain("FLOOR-1", bursaCodes);
        Assert.Contains("FLOOR-1", istanbulCodes);
        Assert.DoesNotContain("PICKING", istanbulCodes);
    }
}
