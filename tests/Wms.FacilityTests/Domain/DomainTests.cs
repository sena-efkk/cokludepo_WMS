using Wms.Modules.Facility.Domain;
using Xunit;

namespace Wms.FacilityTests.Domain;

public sealed class WarehouseDomainTests
{
    [Fact]
    public void Create_rejects_empty_code()
    {
        Assert.Throws<ArgumentException>(() => Warehouse.Create(" ", "Depo"));
    }

    [Fact]
    public void Create_rejects_empty_name()
    {
        Assert.Throws<ArgumentException>(() => Warehouse.Create("BURSA-01", " "));
    }

    [Fact]
    public void Create_uppercases_code()
    {
        var warehouse = Warehouse.Create(" bursa-01 ", "Bursa Deposu");
        Assert.Equal("BURSA-01", warehouse.Code);
    }

    [Theory]
    [InlineData(-91)]
    [InlineData(91)]
    public void Create_rejects_out_of_range_latitude(decimal latitude)
    {
        Assert.Throws<ArgumentException>(() => Warehouse.Create("X-01", "X", latitude: latitude));
    }

    [Theory]
    [InlineData(-181)]
    [InlineData(181)]
    public void Create_rejects_out_of_range_longitude(decimal longitude)
    {
        Assert.Throws<ArgumentException>(() => Warehouse.Create("X-01", "X", longitude: longitude));
    }

    [Fact]
    public void Deactivate_sets_inactive()
    {
        var warehouse = Warehouse.Create("X-01", "X");
        warehouse.Deactivate();
        Assert.False(warehouse.IsActive);
    }
}

public sealed class LocationDomainTests
{
    private static readonly Guid WarehouseId = Guid.NewGuid();

    [Fact]
    public void Create_rejects_empty_warehouse_id()
    {
        Assert.Throws<ArgumentException>(() => Location.Create(Guid.Empty, null, "A01", "A", LocationType.Aisle));
    }

    [Fact]
    public void Create_rejects_empty_code()
    {
        Assert.Throws<ArgumentException>(() => Location.Create(WarehouseId, null, " ", "A", LocationType.Aisle));
    }

    [Fact]
    public void Create_rejects_empty_name()
    {
        Assert.Throws<ArgumentException>(() => Location.Create(WarehouseId, null, "A01", " ", LocationType.Aisle));
    }

    [Fact]
    public void Create_uppercases_code()
    {
        var location = Location.Create(WarehouseId, null, " a01 ", "A", LocationType.Aisle);
        Assert.Equal("A01", location.Code);
    }

    [Fact]
    public void SetParent_rejects_self_parent()
    {
        var location = Location.Create(WarehouseId, null, "A01", "A", LocationType.Aisle);

        Assert.Throws<ArgumentException>(() => location.SetParent(location.Id));
    }

    [Fact]
    public void SetParent_accepts_null()
    {
        var parent = Location.Create(WarehouseId, null, "A01", "A", LocationType.Aisle);
        var child = Location.Create(WarehouseId, parent.Id, "A01-R01", "R", LocationType.Rack);

        child.SetParent(null);

        Assert.Null(child.ParentLocationId);
    }

    [Fact]
    public void Deactivate_sets_inactive()
    {
        var location = Location.Create(WarehouseId, null, "A01", "A", LocationType.Aisle);
        location.Deactivate();
        Assert.False(location.IsActive);
    }
}
