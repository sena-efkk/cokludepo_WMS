namespace Wms.Modules.Inventory.Application;

public class InventoryNotFoundException : Exception
{
    public InventoryNotFoundException(string message)
        : base(message)
    {
    }
}

public sealed class ReservationNotFoundException : InventoryNotFoundException
{
    public ReservationNotFoundException(Guid reservationId)
        : base($"Reservation bulunamadı: {reservationId}")
    {
    }
}

public sealed class InsufficientInventoryException : Exception
{
    public InsufficientInventoryException(Guid warehouseId, Guid skuId, int requested, int available)
        : base($"Yetersiz stok: {requested} istendi, {available} kullanılabilir (warehouse={warehouseId}, sku={skuId}).")
    {
    }
}

public sealed class InvalidReservationStateException : Exception
{
    public InvalidReservationStateException(string message)
        : base(message)
    {
    }
}

public sealed class SkuValidationException : Exception
{
    public SkuValidationException(string message)
        : base(message)
    {
    }
}

public sealed class WarehouseValidationException : Exception
{
    public WarehouseValidationException(string message)
        : base(message)
    {
    }
}

public sealed class LocationValidationException : Exception
{
    public LocationValidationException(string message)
        : base(message)
    {
    }
}
