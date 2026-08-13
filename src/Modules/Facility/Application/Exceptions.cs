namespace Wms.Modules.Facility.Application;

public class FacilityNotFoundException : Exception
{
    public FacilityNotFoundException(string message)
        : base(message)
    {
    }
}

public sealed class WarehouseNotFoundException : FacilityNotFoundException
{
    public WarehouseNotFoundException(Guid warehouseId)
        : base($"Warehouse bulunamadı: {warehouseId}")
    {
    }
}

public sealed class LocationNotFoundException : FacilityNotFoundException
{
    public LocationNotFoundException(Guid locationId)
        : base($"Location bulunamadı: {locationId}")
    {
    }
}

public sealed class DuplicateWarehouseCodeException : Exception
{
    public DuplicateWarehouseCodeException(string code)
        : base($"Warehouse kodu zaten kullanımda: {code}")
    {
    }
}

public sealed class DuplicateLocationCodeException : Exception
{
    public DuplicateLocationCodeException(string code)
        : base($"Bu warehouse'da location kodu zaten kullanımda: {code}")
    {
    }
}

public sealed class LocationWarehouseMismatchException : Exception
{
    public LocationWarehouseMismatchException(string message)
        : base(message)
    {
    }
}

public sealed class LocationCycleException : Exception
{
    public LocationCycleException(string message)
        : base(message)
    {
    }
}
