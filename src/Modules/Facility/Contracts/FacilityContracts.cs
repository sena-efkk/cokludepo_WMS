namespace Wms.Modules.Facility.Contracts;

public sealed record WarehouseInfo(Guid Id, string Code, bool IsActive, decimal? Latitude, decimal? Longitude);

public sealed record LocationInfo(Guid Id, Guid WarehouseId, string Code, bool IsActive, bool HoldsInventory, bool AllowsPicking, string LocationType);

public interface IFacilityQueryContract
{
    Task<WarehouseInfo?> GetWarehouseAsync(Guid warehouseId, CancellationToken cancellationToken);

    Task<LocationInfo?> GetLocationAsync(Guid locationId, CancellationToken cancellationToken);

    Task<LocationInfo?> GetLocationByCodeAsync(Guid warehouseId, string code, CancellationToken cancellationToken);

    Task<LocationInfo?> GetLocationByCodeGlobalAsync(string code, CancellationToken cancellationToken);

    Task<IReadOnlyList<WarehouseInfo>> GetActiveWarehousesAsync(CancellationToken cancellationToken);
}
