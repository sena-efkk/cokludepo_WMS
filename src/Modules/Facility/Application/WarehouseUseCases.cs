using Wms.Modules.Facility.Domain;

namespace Wms.Modules.Facility.Application;

public sealed record CreateWarehouseCommand(
    string Code,
    string Name,
    string? AddressLine,
    string? City,
    string? CountryCode,
    decimal? Latitude,
    decimal? Longitude);

public sealed class CreateWarehouse(IFacilityStore store)
{
    public async Task<Warehouse> Handle(CreateWarehouseCommand command, CancellationToken cancellationToken)
    {
        var code = command.Code.Trim().ToUpperInvariant();
        if (await store.GetWarehouseByCodeAsync(code, cancellationToken) is not null)
        {
            throw new DuplicateWarehouseCodeException(code);
        }

        var warehouse = Warehouse.Create(
            command.Code,
            command.Name,
            command.AddressLine,
            command.City,
            command.CountryCode,
            command.Latitude,
            command.Longitude);

        await store.AddWarehouseAsync(warehouse, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        return warehouse;
    }
}

public sealed class GetWarehouse(IFacilityStore store)
{
    public async Task<Warehouse?> Handle(Guid warehouseId, CancellationToken cancellationToken)
    {
        return await store.GetWarehouseAsync(warehouseId, cancellationToken);
    }
}

public sealed class ListWarehouses(IFacilityStore store)
{
    public async Task<IReadOnlyList<Warehouse>> Handle(string? search, bool includeInactive, CancellationToken cancellationToken)
    {
        return await store.ListWarehousesAsync(search, includeInactive, cancellationToken);
    }
}

public sealed class DeactivateWarehouse(IFacilityStore store)
{
    public async Task Handle(Guid warehouseId, CancellationToken cancellationToken)
    {
        var warehouse = await store.GetWarehouseAsync(warehouseId, cancellationToken)
            ?? throw new WarehouseNotFoundException(warehouseId);

        if (warehouse.IsActive)
        {
            warehouse.Deactivate();
            await store.SaveChangesAsync(cancellationToken);
        }
    }
}
