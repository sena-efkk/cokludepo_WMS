using Wms.Api.Network;
using Wms.Modules.Fulfillment.Application;

namespace Wms.Api.Endpoints;

public static class NetworkInventoryEndpoints
{
    public static IEndpointRouteBuilder MapNetworkInventoryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/network/inventory");

        group.MapGet("/skus/{skuId:guid}", async (Guid skuId, NetworkInventoryView view, CancellationToken ct) =>
        {
            var result = await view.GetSkuAsync(skuId, ct);
            return result is null ? Results.NotFound() : Results.Ok(SkuNetworkResponse.From(result));
        });

        group.MapGet("/skus", async (
            Guid? warehouseId,
            bool? hasStock,
            bool? hasAtp,
            string? riskLevel,
            string? search,
            string? sort,
            int page,
            int pageSize,
            NetworkInventoryView view,
            CancellationToken ct) =>
        {
            var filter = new ListNetworkSkusFilter(
                warehouseId,
                hasStock,
                hasAtp,
                riskLevel,
                search,
                sort,
                page <= 0 ? 1 : page,
                pageSize <= 0 || pageSize > 200 ? 50 : pageSize);
            var result = await view.ListSkusAsync(filter, ct);
            return Results.Ok(NetworkSkuPageResponse.From(result));
        });

        group.MapGet("/warehouses/{warehouseId:guid}", async (
            Guid warehouseId,
            int page,
            int pageSize,
            NetworkInventoryView view,
            CancellationToken ct) =>
        {
            var result = await view.GetWarehouseAsync(
                warehouseId,
                page <= 0 ? 1 : page,
                pageSize <= 0 || pageSize > 200 ? 50 : pageSize,
                ct);
            return result is null ? Results.NotFound() : Results.Ok(WarehouseNetworkResponse.From(result));
        });

        group.MapGet("/summary", async (NetworkInventoryView view, CancellationToken ct) =>
        {
            var summary = await view.GetSummaryAsync(ct);
            return Results.Ok(NetworkSummaryResponse.From(summary));
        });

        group.MapPost("/availability", async (OrderAvailabilityRequest request, NetworkInventoryView view, CancellationToken ct) =>
        {
            if (request.Lines.Count == 0)
            {
                return Results.BadRequest("En az bir line gerekli.");
            }

            var lines = await view.GetOrderAvailabilityAsync(
                request.Lines.Select(l => new OrderAvailabilityLineInput(l.SkuId, l.Quantity)).ToList(),
                ct);
            return Results.Ok(OrderAvailabilityResponse.From(lines));
        });

        return endpoints;
    }
}
