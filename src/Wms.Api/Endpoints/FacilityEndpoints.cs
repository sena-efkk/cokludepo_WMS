using Wms.Api.Facility;
using Wms.Modules.Facility.Application;
using Wms.Modules.Facility.Application.Seed;
using Wms.Modules.Facility.Domain;

namespace Wms.Api.Endpoints;

public static class FacilityEndpoints
{
    public static IEndpointRouteBuilder MapFacilityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api");

        group.MapPost("/warehouses", async (CreateWarehouseRequest request, CreateWarehouse useCase, CancellationToken ct) =>
            await HandleAsync(
                async () =>
                {
                    var warehouse = await useCase.Handle(
                        new CreateWarehouseCommand(
                            request.Code,
                            request.Name,
                            request.AddressLine,
                            request.City,
                            request.CountryCode,
                            request.Latitude,
                            request.Longitude),
                        ct);
                    return Results.Created($"/api/warehouses/{warehouse.Id}", WarehouseResponse.From(warehouse));
                },
                ct));

        group.MapGet("/warehouses", async (ListWarehouses useCase, string? search, bool includeInactive = false, CancellationToken ct = default) =>
        {
            var warehouses = await useCase.Handle(search, includeInactive, ct);
            return Results.Ok(warehouses.Select(WarehouseResponse.From));
        });

        group.MapGet("/warehouses/{id:guid}", async (Guid id, GetWarehouse useCase, CancellationToken ct) =>
        {
            var warehouse = await useCase.Handle(id, ct);
            return warehouse is null ? Results.NotFound() : Results.Ok(WarehouseResponse.From(warehouse));
        });

        group.MapPost("/warehouses/{id:guid}/deactivate", async (Guid id, DeactivateWarehouse useCase, CancellationToken ct) =>
            await HandleAsync(
                async () =>
                {
                    await useCase.Handle(id, ct);
                    return Results.NoContent();
                },
                ct));

        group.MapPost("/warehouses/{warehouseId:guid}/locations", async (Guid warehouseId, CreateLocationRequest request, CreateLocation useCase, CancellationToken ct) =>
            await HandleAsync(
                async () =>
                {
                    var location = await useCase.Handle(
                        new CreateLocationCommand(
                            warehouseId,
                            request.ParentLocationId,
                            request.Code,
                            request.Name,
                            Enum.Parse<LocationType>(request.Type, ignoreCase: true),
                            request.AllowsPicking,
                            request.AllowsPutaway,
                            request.AllowsReplenishment,
                            request.HoldsInventory),
                        ct);
                    return Results.Created($"/api/warehouses/{warehouseId}/locations/{location.Id}", LocationResponse.From(location));
                },
                ct));

        group.MapGet("/warehouses/{warehouseId:guid}/locations", async (Guid warehouseId, Guid? parentId, ListLocations useCase, bool includeInactive = false, CancellationToken ct = default) =>
            await HandleAsync(
                async () =>
                {
                    var locations = await useCase.Handle(warehouseId, parentId, includeInactive, ct);
                    return Results.Ok(locations.Select(LocationResponse.From));
                },
                ct));

        group.MapGet("/warehouses/{warehouseId:guid}/locations/{id:guid}", async (Guid warehouseId, Guid id, GetLocation useCase, CancellationToken ct) =>
        {
            var location = await useCase.Handle(warehouseId, id, ct);
            return location is null ? Results.NotFound() : Results.Ok(LocationResponse.From(location));
        });

        group.MapPost("/warehouses/{warehouseId:guid}/locations/{id:guid}/deactivate", async (Guid warehouseId, Guid id, DeactivateLocation useCase, CancellationToken ct) =>
            await HandleAsync(
                async () =>
                {
                    await useCase.Handle(warehouseId, id, ct);
                    return Results.NoContent();
                },
                ct));

        group.MapPost("/warehouses/{warehouseId:guid}/locations/{id:guid}/parent", async (Guid warehouseId, Guid id, ReparentLocationRequest request, ReparentLocation useCase, CancellationToken ct) =>
            await HandleAsync(
                async () =>
                {
                    await useCase.Handle(warehouseId, id, request.ParentLocationId, ct);
                    return Results.NoContent();
                },
                ct));

        group.MapGet("/warehouses/{warehouseId:guid}/location-tree", async (Guid warehouseId, GetLocationTree useCase, bool includeInactive = false, CancellationToken ct = default) =>
            await HandleAsync(
                async () =>
                {
                    var tree = await useCase.Handle(warehouseId, includeInactive, ct);
                    return Results.Ok(tree.Select(ToNode));
                },
                ct));

        group.MapPost("/facility/seed-demo", async (SeedDemoFacilities useCase, CancellationToken ct) =>
        {
            var result = await useCase.Handle(SyntheticFacilityFactory.CreatePlans(), ct);
            return Results.Ok(new FacilitySeedResponse(result.WarehousesCreated, result.LocationsCreated, result.Skipped));
        });

        return endpoints;
    }

    private static LocationTreeNodeResponse ToNode(Wms.Modules.Facility.Application.LocationTreeNode node) =>
        new(node.Id, node.Code, node.Name, node.Type, node.IsActive, node.Children.Select(ToNode).ToList());

    private static async Task<IResult> HandleAsync(Func<Task<IResult>> action, CancellationToken ct)
    {
        try
        {
            return await action();
        }
        catch (FacilityNotFoundException exception)
        {
            return Results.NotFound(exception.Message);
        }
        catch (DuplicateWarehouseCodeException exception)
        {
            return Results.Conflict(exception.Message);
        }
        catch (DuplicateLocationCodeException exception)
        {
            return Results.Conflict(exception.Message);
        }
        catch (LocationWarehouseMismatchException exception)
        {
            return Results.BadRequest(exception.Message);
        }
        catch (LocationCycleException exception)
        {
            return Results.Conflict(exception.Message);
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(exception.Message);
        }
    }
}
