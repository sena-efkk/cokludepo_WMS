using Wms.Modules.Facility.Application;
using Wms.Modules.Facility.Domain;
using Xunit;

namespace Wms.FacilityTests.Application;

public sealed class WarehouseUseCaseTests
{
    [Fact]
    public async Task CreateWarehouse_rejects_duplicate_code()
    {
        var store = new FakeFacilityStore();
        store.Warehouses.Add(Warehouse.Create("BURSA-01", "Bursa"));
        var useCase = new CreateWarehouse(store);

        await Assert.ThrowsAsync<DuplicateWarehouseCodeException>(() => useCase.Handle(
            new CreateWarehouseCommand("bursa-01", "Başka Bursa", null, null, null, null, null),
            CancellationToken.None));
    }

    [Fact]
    public async Task CreateWarehouse_creates_and_persists()
    {
        var store = new FakeFacilityStore();
        var useCase = new CreateWarehouse(store);

        var warehouse = await useCase.Handle(
            new CreateWarehouseCommand("IST-01", "İstanbul Deposu", null, "İstanbul", "TR", 41.0082m, 28.9784m),
            CancellationToken.None);

        Assert.Equal("IST-01", warehouse.Code);
        Assert.Single(store.Warehouses);
        Assert.Equal(1, store.SaveChangesCount);
    }

    [Fact]
    public async Task DeactivateWarehouse_throws_when_missing()
    {
        var store = new FakeFacilityStore();
        var useCase = new DeactivateWarehouse(store);

        await Assert.ThrowsAsync<WarehouseNotFoundException>(() => useCase.Handle(Guid.NewGuid(), CancellationToken.None));
    }
}

public sealed class LocationUseCaseTests
{
    private readonly Warehouse _warehouseA = Warehouse.Create("A-01", "Depo A");
    private readonly Warehouse _warehouseB = Warehouse.Create("B-01", "Depo B");

    private FakeFacilityStore NewStore()
    {
        var store = new FakeFacilityStore();
        store.Warehouses.Add(_warehouseA);
        store.Warehouses.Add(_warehouseB);
        return store;
    }

    [Fact]
    public async Task CreateLocation_rejects_duplicate_code_in_same_warehouse()
    {
        var store = NewStore();
        store.Locations.Add(Location.Create(_warehouseA.Id, null, "A01", "Koridor", LocationType.Aisle));
        var useCase = new CreateLocation(store);

        await Assert.ThrowsAsync<DuplicateLocationCodeException>(() => useCase.Handle(
            new CreateLocationCommand(_warehouseA.Id, null, "a01", "Başka", LocationType.Aisle, false, false, false, false),
            CancellationToken.None));
    }

    [Fact]
    public async Task CreateLocation_allows_same_code_in_different_warehouses()
    {
        var store = NewStore();
        store.Locations.Add(Location.Create(_warehouseA.Id, null, "A01", "Koridor A", LocationType.Aisle));
        var useCase = new CreateLocation(store);

        var location = await useCase.Handle(
            new CreateLocationCommand(_warehouseB.Id, null, "A01", "Koridor B", LocationType.Aisle, false, false, false, false),
            CancellationToken.None);

        Assert.Equal("A01", location.Code);
    }

    [Fact]
    public async Task CreateLocation_rejects_parent_from_other_warehouse()
    {
        var store = NewStore();
        var parentInB = Location.Create(_warehouseB.Id, null, "P01", "Parent B", LocationType.Zone);
        store.Locations.Add(parentInB);
        var useCase = new CreateLocation(store);

        await Assert.ThrowsAsync<LocationWarehouseMismatchException>(() => useCase.Handle(
            new CreateLocationCommand(_warehouseA.Id, parentInB.Id, "C01", "Child", LocationType.Bin, false, false, false, false),
            CancellationToken.None));
    }

    [Fact]
    public async Task CreateLocation_rejects_unknown_warehouse()
    {
        var store = NewStore();
        var useCase = new CreateLocation(store);

        await Assert.ThrowsAsync<WarehouseNotFoundException>(() => useCase.Handle(
            new CreateLocationCommand(Guid.NewGuid(), null, "A01", "A", LocationType.Aisle, false, false, false, false),
            CancellationToken.None));
    }

    [Fact]
    public async Task ReparentLocation_rejects_cycle()
    {
        var store = NewStore();
        var a = Location.Create(_warehouseA.Id, null, "A", "A", LocationType.Zone);
        var b = Location.Create(_warehouseA.Id, a.Id, "B", "B", LocationType.Zone);
        var c = Location.Create(_warehouseA.Id, b.Id, "C", "C", LocationType.Zone);
        store.Locations.AddRange([a, b, c]);
        var useCase = new ReparentLocation(store);

        await Assert.ThrowsAsync<LocationCycleException>(() => useCase.Handle(_warehouseA.Id, a.Id, c.Id, CancellationToken.None));
    }

    [Fact]
    public async Task ReparentLocation_moves_within_same_warehouse()
    {
        var store = NewStore();
        var a = Location.Create(_warehouseA.Id, null, "A", "A", LocationType.Zone);
        var b = Location.Create(_warehouseA.Id, a.Id, "B", "B", LocationType.Zone);
        var c = Location.Create(_warehouseA.Id, b.Id, "C", "C", LocationType.Zone);
        var d = Location.Create(_warehouseA.Id, null, "D", "D", LocationType.Zone);
        store.Locations.AddRange([a, b, c, d]);
        var useCase = new ReparentLocation(store);

        await useCase.Handle(_warehouseA.Id, c.Id, d.Id, CancellationToken.None);

        Assert.Equal(d.Id, c.ParentLocationId);
    }

    [Fact]
    public async Task ReparentLocation_rejects_cross_warehouse_parent()
    {
        var store = NewStore();
        var a = Location.Create(_warehouseA.Id, null, "A", "A", LocationType.Zone);
        var bParent = Location.Create(_warehouseB.Id, null, "BP", "B Parent", LocationType.Zone);
        store.Locations.AddRange([a, bParent]);
        var useCase = new ReparentLocation(store);

        await Assert.ThrowsAsync<LocationWarehouseMismatchException>(() => useCase.Handle(_warehouseA.Id, a.Id, bParent.Id, CancellationToken.None));
    }
}

public sealed class LocationTreeTests
{
    [Fact]
    public void BuildTree_produces_expected_hierarchy()
    {
        var warehouseId = Guid.NewGuid();
        var pick = Location.Create(warehouseId, null, "PICKING", "Toplama", LocationType.Picking);
        var aisle = Location.Create(warehouseId, pick.Id, "A01", "Koridor", LocationType.Aisle);
        var rack = Location.Create(warehouseId, aisle.Id, "A01-R01", "Raf", LocationType.Rack);
        var bin1 = Location.Create(warehouseId, rack.Id, "A01-R01-B01", "Göz 1", LocationType.Bin);
        var bin2 = Location.Create(warehouseId, rack.Id, "A01-R01-B02", "Göz 2", LocationType.Bin);
        var receiving = Location.Create(warehouseId, null, "RECEIVING", "Giriş", LocationType.Receiving);

        var tree = GetLocationTree.BuildTree([receiving, bin2, aisle, pick, bin1, rack]);

        Assert.Equal(2, tree.Count);
        var picking = tree.Single(n => n.Code == "PICKING");
        var a01 = Assert.Single(picking.Children);
        var r01 = Assert.Single(a01.Children);
        Assert.Equal(2, r01.Children.Count);
        Assert.Contains(r01.Children, n => n.Code == "A01-R01-B01");
        Assert.Contains(tree, n => n.Code == "RECEIVING");
    }
}

public sealed class SeedDemoFacilitiesTests
{
    [Fact]
    public async Task Seed_is_idempotent()
    {
        var store = new FakeFacilityStore();
        var useCase = new Wms.Modules.Facility.Application.Seed.SeedDemoFacilities(store);
        var plans = Wms.Modules.Facility.Application.Seed.SyntheticFacilityFactory.CreatePlans();

        var first = await useCase.Handle(plans, CancellationToken.None);
        var second = await useCase.Handle(plans, CancellationToken.None);

        Assert.Equal(plans.Count, first.WarehousesCreated);
        Assert.True(first.LocationsCreated > 0);
        Assert.Equal(0, second.WarehousesCreated);
        Assert.Equal(0, second.LocationsCreated);
        Assert.Equal(plans.Sum(p => p.Locations.Count), second.Skipped);
    }
}
