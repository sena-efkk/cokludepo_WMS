using Wms.Modules.Facility.Domain;

namespace Wms.Api.Facility;

public sealed record CreateWarehouseRequest(
    string Code,
    string Name,
    string? AddressLine,
    string? City,
    string? CountryCode,
    decimal? Latitude,
    decimal? Longitude);

public sealed record WarehouseResponse(
    Guid Id,
    string Code,
    string Name,
    string? AddressLine,
    string? City,
    string? CountryCode,
    decimal? Latitude,
    decimal? Longitude,
    bool IsActive)
{
    public static WarehouseResponse From(Warehouse warehouse) =>
        new(
            warehouse.Id,
            warehouse.Code,
            warehouse.Name,
            warehouse.AddressLine,
            warehouse.City,
            warehouse.CountryCode,
            warehouse.Latitude,
            warehouse.Longitude,
            warehouse.IsActive);
}

public sealed record CreateLocationRequest(
    string Code,
    string Name,
    string Type,
    Guid? ParentLocationId,
    bool AllowsPicking,
    bool AllowsPutaway,
    bool AllowsReplenishment,
    bool HoldsInventory);

public sealed record LocationResponse(
    Guid Id,
    Guid WarehouseId,
    Guid? ParentLocationId,
    string Code,
    string Name,
    string Type,
    bool AllowsPicking,
    bool AllowsPutaway,
    bool AllowsReplenishment,
    bool HoldsInventory,
    bool IsActive)
{
    public static LocationResponse From(Location location) =>
        new(
            location.Id,
            location.WarehouseId,
            location.ParentLocationId,
            location.Code,
            location.Name,
            location.Type.ToString(),
            location.AllowsPicking,
            location.AllowsPutaway,
            location.AllowsReplenishment,
            location.HoldsInventory,
            location.IsActive);
}

public sealed record LocationTreeNodeResponse(
    Guid Id,
    string Code,
    string Name,
    string Type,
    bool IsActive,
    IReadOnlyList<LocationTreeNodeResponse> Children);

public sealed record ReparentLocationRequest(Guid? ParentLocationId);

public sealed record FacilitySeedResponse(int WarehousesCreated, int LocationsCreated, int Skipped);
