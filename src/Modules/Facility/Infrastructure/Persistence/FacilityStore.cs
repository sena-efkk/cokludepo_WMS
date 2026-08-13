using Microsoft.EntityFrameworkCore;
using Npgsql.EntityFrameworkCore.PostgreSQL;
using Wms.Modules.Facility.Application;
using Wms.Modules.Facility.Domain;

namespace Wms.Modules.Facility.Infrastructure.Persistence;

public sealed class FacilityStore(FacilityDbContext db) : IFacilityStore
{
    public async Task<Warehouse?> GetWarehouseAsync(Guid id, CancellationToken cancellationToken)
    {
        return await db.Warehouses.FirstOrDefaultAsync(w => w.Id == id, cancellationToken);
    }

    public async Task<Warehouse?> GetWarehouseByCodeAsync(string code, CancellationToken cancellationToken)
    {
        return await db.Warehouses.FirstOrDefaultAsync(w => w.Code == code, cancellationToken);
    }

    public async Task<IReadOnlyList<Warehouse>> ListWarehousesAsync(string? search, bool includeInactive, CancellationToken cancellationToken)
    {
        var query = db.Warehouses.AsNoTracking().AsQueryable();

        if (!includeInactive)
        {
            query = query.Where(w => w.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search.Trim()}%";
            query = query.Where(w =>
                EF.Functions.ILike(w.Code, term)
                || EF.Functions.ILike(w.Name, term)
                || (w.City != null && EF.Functions.ILike(w.City, term)));
        }

        var result = await query.OrderBy(w => w.Code).ToListAsync(cancellationToken);
        return result.AsReadOnly();
    }

    public async Task AddWarehouseAsync(Warehouse warehouse, CancellationToken cancellationToken)
    {
        await db.Warehouses.AddAsync(warehouse, cancellationToken);
    }

    public async Task<Location?> GetLocationAsync(Guid id, CancellationToken cancellationToken)
    {
        return await db.Locations.FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
    }

    public async Task<Location?> GetLocationByCodeAsync(Guid warehouseId, string code, CancellationToken cancellationToken)
    {
        return await db.Locations.FirstOrDefaultAsync(
            l => l.WarehouseId == warehouseId && l.Code == code,
            cancellationToken);
    }

    public async Task<IReadOnlyList<Location>> ListLocationsAsync(Guid warehouseId, bool includeInactive, CancellationToken cancellationToken)
    {
        var query = db.Locations.AsNoTracking().Where(l => l.WarehouseId == warehouseId);

        if (!includeInactive)
        {
            query = query.Where(l => l.IsActive);
        }

        var result = await query.OrderBy(l => l.Code).ToListAsync(cancellationToken);
        return result.AsReadOnly();
    }

    public async Task AddLocationAsync(Location location, CancellationToken cancellationToken)
    {
        await db.Locations.AddAsync(location, cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return db.SaveChangesAsync(cancellationToken);
    }
}
