using Wms.Modules.Facility.Domain;

namespace Wms.Modules.Facility.Application;

public sealed record CreateLocationCommand(
    Guid WarehouseId,
    Guid? ParentLocationId,
    string Code,
    string Name,
    LocationType Type,
    bool AllowsPicking,
    bool AllowsPutaway,
    bool AllowsReplenishment,
    bool HoldsInventory);

public sealed class CreateLocation(IFacilityStore store)
{
    public async Task<Location> Handle(CreateLocationCommand command, CancellationToken cancellationToken)
    {
        var warehouse = await store.GetWarehouseAsync(command.WarehouseId, cancellationToken)
            ?? throw new WarehouseNotFoundException(command.WarehouseId);

        if (command.ParentLocationId is { } parentId)
        {
            var parent = await store.GetLocationAsync(parentId, cancellationToken)
                ?? throw new LocationNotFoundException(parentId);

            if (parent.WarehouseId != command.WarehouseId)
            {
                throw new LocationWarehouseMismatchException(
                    $"Parent location {parent.Code} farklı bir warehouse'a aittir — parent ve child aynı warehouse'da olmalıdır.");
            }
        }

        var code = command.Code.Trim().ToUpperInvariant();
        if (await store.GetLocationByCodeAsync(command.WarehouseId, code, cancellationToken) is not null)
        {
            throw new DuplicateLocationCodeException(code);
        }

        var location = Location.Create(
            command.WarehouseId,
            command.ParentLocationId,
            command.Code,
            command.Name,
            command.Type,
            command.AllowsPicking,
            command.AllowsPutaway,
            command.AllowsReplenishment,
            command.HoldsInventory);

        await store.AddLocationAsync(location, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        return location;
    }
}

public sealed class GetLocation(IFacilityStore store)
{
    public async Task<Location?> Handle(Guid warehouseId, Guid locationId, CancellationToken cancellationToken)
    {
        var location = await store.GetLocationAsync(locationId, cancellationToken);
        return location is not null && location.WarehouseId == warehouseId ? location : null;
    }
}

public sealed class ListLocations(IFacilityStore store)
{
    public async Task<IReadOnlyList<Location>> Handle(
        Guid warehouseId,
        Guid? parentId,
        bool includeInactive,
        CancellationToken cancellationToken)
    {
        var warehouse = await store.GetWarehouseAsync(warehouseId, cancellationToken)
            ?? throw new WarehouseNotFoundException(warehouseId);

        var locations = await store.ListLocationsAsync(warehouseId, includeInactive, cancellationToken);
        return locations
            .Where(l => parentId is null || l.ParentLocationId == parentId)
            .OrderBy(l => l.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

public sealed class DeactivateLocation(IFacilityStore store)
{
    public async Task Handle(Guid warehouseId, Guid locationId, CancellationToken cancellationToken)
    {
        var location = await store.GetLocationAsync(locationId, cancellationToken)
            ?? throw new LocationNotFoundException(locationId);

        if (location.WarehouseId != warehouseId)
        {
            throw new LocationNotFoundException(locationId);
        }

        if (location.IsActive)
        {
            location.Deactivate();
            await store.SaveChangesAsync(cancellationToken);
        }
    }
}

public sealed class ReparentLocation(IFacilityStore store)
{
    public async Task Handle(Guid warehouseId, Guid locationId, Guid? newParentLocationId, CancellationToken cancellationToken)
    {
        var location = await store.GetLocationAsync(locationId, cancellationToken)
            ?? throw new LocationNotFoundException(locationId);

        if (location.WarehouseId != warehouseId)
        {
            throw new LocationNotFoundException(locationId);
        }

        if (newParentLocationId is not { } newParentId)
        {
            location.SetParent(null);
            await store.SaveChangesAsync(cancellationToken);
            return;
        }

        var newParent = await store.GetLocationAsync(newParentId, cancellationToken)
            ?? throw new LocationNotFoundException(newParentId);

        if (newParent.WarehouseId != warehouseId)
        {
            throw new LocationWarehouseMismatchException(
                $"Parent location {newParent.Code} farklı bir warehouse'a aittir — parent ve child aynı warehouse'da olmalıdır.");
        }

        if (await CreatesCycleAsync(locationId, newParent, cancellationToken))
        {
            throw new LocationCycleException(
                $"Bu taşıma hiyerarşide cycle oluşturur: {location.Code} -> {newParent.Code}.");
        }

        location.SetParent(newParentId);
        await store.SaveChangesAsync(cancellationToken);
    }

    private async Task<bool> CreatesCycleAsync(Guid locationId, Location newParent, CancellationToken cancellationToken)
    {
        var current = newParent;
        var visited = new HashSet<Guid> { locationId };

        while (current.ParentLocationId is { } ancestorId)
        {
            if (!visited.Add(ancestorId))
            {
                return true;
            }

            if (ancestorId == locationId)
            {
                return true;
            }

            var ancestor = await store.GetLocationAsync(ancestorId, cancellationToken)
                ?? throw new LocationNotFoundException(ancestorId);
            current = ancestor;
        }

        return false;
    }
}
