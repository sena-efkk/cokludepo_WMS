using Wms.Modules.Facility.Domain;

namespace Wms.Modules.Facility.Application;

public sealed record LocationTreeNode(
    Guid Id,
    string Code,
    string Name,
    string Type,
    bool IsActive,
    IReadOnlyList<LocationTreeNode> Children);

public sealed class GetLocationTree(IFacilityStore store)
{
    public async Task<IReadOnlyList<LocationTreeNode>> Handle(
        Guid warehouseId,
        bool includeInactive,
        CancellationToken cancellationToken)
    {
        var warehouse = await store.GetWarehouseAsync(warehouseId, cancellationToken)
            ?? throw new WarehouseNotFoundException(warehouseId);

        var locations = await store.ListLocationsAsync(warehouseId, includeInactive, cancellationToken);
        return BuildTree(locations);
    }

    public static IReadOnlyList<LocationTreeNode> BuildTree(IReadOnlyList<Location> locations)
    {
        var byParent = locations
            .GroupBy(l => l.ParentLocationId ?? Guid.Empty)
            .ToDictionary(g => g.Key, g => g.OrderBy(l => l.Code, StringComparer.OrdinalIgnoreCase).ToList());

        IReadOnlyList<LocationTreeNode> Children(Guid id) =>
            byParent.TryGetValue(id, out var children)
                ? children.Select(l => new LocationTreeNode(
                    l.Id,
                    l.Code,
                    l.Name,
                    l.Type.ToString(),
                    l.IsActive,
                    Children(l.Id))).ToList()
                : [];

        return byParent.TryGetValue(Guid.Empty, out var roots)
            ? roots.Select(l => new LocationTreeNode(
                l.Id,
                l.Code,
                l.Name,
                l.Type.ToString(),
                l.IsActive,
                Children(l.Id))).ToList()
            : [];
    }
}
