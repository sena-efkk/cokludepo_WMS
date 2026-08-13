using Wms.Modules.Facility.Application;
using Wms.Modules.Facility.Domain;

namespace Wms.FacilityTests.Application;

public sealed class FakeFacilityStore : IFacilityStore
{
    public List<Warehouse> Warehouses { get; } = [];

    public List<Location> Locations { get; } = [];

    public int SaveChangesCount { get; private set; }

    public Task<Warehouse?> GetWarehouseAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Warehouses.FirstOrDefault(w => w.Id == id));

    public Task<Warehouse?> GetWarehouseByCodeAsync(string code, CancellationToken cancellationToken) =>
        Task.FromResult(Warehouses.FirstOrDefault(w => w.Code == code));

    public Task<IReadOnlyList<Warehouse>> ListWarehousesAsync(string? search, bool includeInactive, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Warehouse>>(Warehouses
            .Where(w => includeInactive || w.IsActive)
            .Where(w => search is null
                || w.Code.Contains(search, StringComparison.OrdinalIgnoreCase)
                || w.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
            .ToList());

    public Task AddWarehouseAsync(Warehouse warehouse, CancellationToken cancellationToken)
    {
        Warehouses.Add(warehouse);
        return Task.CompletedTask;
    }

    public Task<Location?> GetLocationAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Locations.FirstOrDefault(l => l.Id == id));

    public Task<Location?> GetLocationByCodeAsync(Guid warehouseId, string code, CancellationToken cancellationToken) =>
        Task.FromResult(Locations.FirstOrDefault(l => l.WarehouseId == warehouseId && l.Code == code));

    public Task<IReadOnlyList<Location>> ListLocationsAsync(Guid warehouseId, bool includeInactive, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Location>>(Locations
            .Where(l => l.WarehouseId == warehouseId)
            .Where(l => includeInactive || l.IsActive)
            .ToList());

    public Task AddLocationAsync(Location location, CancellationToken cancellationToken)
    {
        Locations.Add(location);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveChangesCount++;
        return Task.CompletedTask;
    }
}
