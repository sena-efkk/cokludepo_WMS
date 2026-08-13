using Wms.Modules.Inventory.Domain;
using Xunit;

namespace Wms.InventoryTests.Domain;

public sealed class InventoryBalanceDomainTests
{
    private static readonly Guid SkuId = Guid.NewGuid();
    private static readonly Guid WarehouseId = Guid.NewGuid();
    private static readonly Guid LocationId = Guid.NewGuid();

    [Fact]
    public void Create_rejects_negative_quantity()
    {
        Assert.Throws<ArgumentException>(() =>
            InventoryBalance.Create(SkuId, WarehouseId, LocationId, InventoryStatus.Available, -1));
    }

    [Fact]
    public void Available_is_derived_only_for_available_status()
    {
        var available = InventoryBalance.Create(SkuId, WarehouseId, LocationId, InventoryStatus.Available, 100);
        var hold = InventoryBalance.Create(SkuId, WarehouseId, LocationId, InventoryStatus.Hold, 50);

        Assert.Equal(100, available.Available);
        Assert.Equal(0, hold.Available);
    }

    [Fact]
    public void AddAllocated_rejects_non_available_status()
    {
        var hold = InventoryBalance.Create(SkuId, WarehouseId, LocationId, InventoryStatus.Hold, 50);

        Assert.Throws<InvalidOperationException>(() => hold.AddAllocated(5));
    }

    [Fact]
    public void AddAllocated_rejects_exceeding_quantity()
    {
        var available = InventoryBalance.Create(SkuId, WarehouseId, LocationId, InventoryStatus.Available, 10);

        Assert.Throws<InvalidOperationException>(() => available.AddAllocated(11));
    }

    [Fact]
    public void Consume_reduces_both_quantity_and_allocated()
    {
        var balance = InventoryBalance.Create(SkuId, WarehouseId, LocationId, InventoryStatus.Available, 100);
        balance.AddAllocated(30);

        balance.Consume(20);

        Assert.Equal(80, balance.Quantity);
        Assert.Equal(10, balance.Allocated);
    }
}

public sealed class ReservationDomainTests
{
    [Fact]
    public void Create_rejects_non_positive_quantity()
    {
        Assert.Throws<ArgumentException>(() =>
            InventoryReservation.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 0));
    }

    [Fact]
    public void Lines_cannot_exceed_requested_quantity()
    {
        var reservation = InventoryReservation.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 5);
        reservation.AddLine(Guid.NewGuid(), 3);

        Assert.Throws<InvalidOperationException>(() => reservation.AddLine(Guid.NewGuid(), 3));
    }

    [Fact]
    public void Released_reservation_cannot_be_consumed()
    {
        var reservation = InventoryReservation.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 5);
        reservation.MarkReleased();

        Assert.Throws<InvalidOperationException>(() => reservation.MarkConsumed());
    }

    [Fact]
    public void Consumed_reservation_cannot_be_released()
    {
        var reservation = InventoryReservation.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 5);
        reservation.MarkConsumed();

        Assert.Throws<InvalidOperationException>(() => reservation.MarkReleased());
    }
}
