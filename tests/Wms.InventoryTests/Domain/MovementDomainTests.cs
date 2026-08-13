using Wms.Modules.Inventory.Domain;
using Xunit;

namespace Wms.InventoryTests.Domain;

public sealed class MovementDomainTests
{
    private static readonly Guid SkuId = Guid.NewGuid();
    private static readonly Guid WarehouseId = Guid.NewGuid();

    [Fact]
    public void CreateRelocate_rejects_same_source_and_destination()
    {
        var location = Guid.NewGuid();

        Assert.Throws<ArgumentException>(() =>
            InventoryMovement.CreateRelocate(Guid.NewGuid(), SkuId, WarehouseId, location, location, 5));
    }

    [Fact]
    public void CreateRelocate_rejects_non_positive_quantity()
    {
        Assert.Throws<ArgumentException>(() =>
            InventoryMovement.CreateRelocate(Guid.NewGuid(), SkuId, WarehouseId, Guid.NewGuid(), Guid.NewGuid(), 0));
    }

    [Fact]
    public void CreateRelocate_has_available_status_both_sides()
    {
        var movement = InventoryMovement.CreateRelocate(
            Guid.NewGuid(), SkuId, WarehouseId, Guid.NewGuid(), Guid.NewGuid(), 7);

        Assert.Equal(MovementType.Relocate, movement.Type);
        Assert.Equal(InventoryStatus.Available, movement.StatusFrom);
        Assert.Equal(InventoryStatus.Available, movement.StatusTo);
    }

    [Fact]
    public void CreateStatusChange_rejects_same_status()
    {
        Assert.Throws<ArgumentException>(() =>
            InventoryMovement.CreateStatusChange(
                Guid.NewGuid(), SkuId, WarehouseId, Guid.NewGuid(), InventoryStatus.Hold, InventoryStatus.Hold, 3));
    }

    [Fact]
    public void CreateStatusChange_sets_location_on_both_sides()
    {
        var location = Guid.NewGuid();
        var movement = InventoryMovement.CreateStatusChange(
            Guid.NewGuid(), SkuId, WarehouseId, location, InventoryStatus.Available, InventoryStatus.Quarantine, 4);

        Assert.Equal(MovementType.StatusChange, movement.Type);
        Assert.Equal(location, movement.SourceLocationId);
        Assert.Equal(location, movement.DestinationLocationId);
        Assert.Equal(InventoryStatus.Available, movement.StatusFrom);
        Assert.Equal(InventoryStatus.Quarantine, movement.StatusTo);
    }
}

public sealed class BalanceMovementDomainTests
{
    [Fact]
    public void DecreaseQuantityUnallocated_rejects_more_than_free_stock()
    {
        var balance = InventoryBalance.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), InventoryStatus.Available, 10);
        balance.AddAllocated(7);

        Assert.Throws<InvalidOperationException>(() => balance.DecreaseQuantityUnallocated(4));
    }

    [Fact]
    public void DecreaseQuantityUnallocated_allows_free_portion()
    {
        var balance = InventoryBalance.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), InventoryStatus.Available, 10);
        balance.AddAllocated(7);

        balance.DecreaseQuantityUnallocated(3);

        Assert.Equal(7, balance.Quantity);
        Assert.Equal(7, balance.Allocated);
    }

    [Fact]
    public void UnallocatedQuantity_is_full_quantity_for_non_available_statuses()
    {
        var hold = InventoryBalance.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), InventoryStatus.Hold, 10);
        var available = InventoryBalance.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), InventoryStatus.Available, 10);
        available.AddAllocated(4);

        Assert.Equal(10, hold.UnallocatedQuantity);
        Assert.Equal(6, available.UnallocatedQuantity);
    }
}
