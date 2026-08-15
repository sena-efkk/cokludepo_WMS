using Microsoft.EntityFrameworkCore;
using Wms.Modules.Facility.Contracts;

namespace Wms.Modules.Facility.Infrastructure.Persistence;

public sealed class FacilityQueryContract(FacilityDbContext db) : IFacilityQueryContract
{
    public async Task<WarehouseInfo?> GetWarehouseAsync(Guid warehouseId, CancellationToken cancellationToken)
    {
        return await db.Warehouses
            .Where(w => w.Id == warehouseId)
            .Select(w => new WarehouseInfo(w.Id, w.Code, w.IsActive, w.Latitude, w.Longitude))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<LocationInfo?> GetLocationAsync(Guid locationId, CancellationToken cancellationToken)
    {
        return await db.Locations
            .Where(l => l.Id == locationId)
            .Select(l => new LocationInfo(l.Id, l.WarehouseId, l.Code, l.IsActive, l.HoldsInventory, l.AllowsPicking, l.Type.ToString()))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<LocationInfo?> GetLocationByCodeAsync(Guid warehouseId, string code, CancellationToken cancellationToken)
    {
        return await db.Locations
            .Where(l => l.WarehouseId == warehouseId && l.Code == code.ToUpperInvariant())
            .Select(l => new LocationInfo(l.Id, l.WarehouseId, l.Code, l.IsActive, l.HoldsInventory, l.AllowsPicking, l.Type.ToString()))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<LocationInfo?> GetLocationByCodeGlobalAsync(string code, CancellationToken cancellationToken)
    {
        return await db.Locations
            .Where(l => l.Code == code.ToUpperInvariant())
            .Select(l => new LocationInfo(l.Id, l.WarehouseId, l.Code, l.IsActive, l.HoldsInventory, l.AllowsPicking, l.Type.ToString()))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<WarehouseInfo>> GetActiveWarehousesAsync(CancellationToken cancellationToken)
    {
        var result = await db.Warehouses
            .Where(w => w.IsActive)
            .Select(w => new WarehouseInfo(w.Id, w.Code, w.IsActive, w.Latitude, w.Longitude))
            .ToListAsync(cancellationToken);
        return result.AsReadOnly();
    }
}
