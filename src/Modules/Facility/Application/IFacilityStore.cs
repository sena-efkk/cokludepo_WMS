using Wms.Modules.Facility.Domain;

namespace Wms.Modules.Facility.Application;

public interface IFacilityStore
{
    Task<Warehouse?> GetWarehouseAsync(Guid id, CancellationToken cancellationToken);

    Task<Warehouse?> GetWarehouseByCodeAsync(string code, CancellationToken cancellationToken);

    Task<IReadOnlyList<Warehouse>> ListWarehousesAsync(string? search, bool includeInactive, CancellationToken cancellationToken);

    Task AddWarehouseAsync(Warehouse warehouse, CancellationToken cancellationToken);

    Task<Location?> GetLocationAsync(Guid id, CancellationToken cancellationToken);

    Task<Location?> GetLocationByCodeAsync(Guid warehouseId, string code, CancellationToken cancellationToken);

    Task<IReadOnlyList<Location>> ListLocationsAsync(Guid warehouseId, bool includeInactive, CancellationToken cancellationToken);

    Task AddLocationAsync(Location location, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
