using Wms.Api.MasterData;
using Wms.Modules.MasterData.Application;
using Wms.Modules.MasterData.Application.Import;

namespace Wms.Api.Endpoints;

public static class MasterDataEndpoints
{
    public static IEndpointRouteBuilder MapMasterDataEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api");

        group.MapPost("/products", async (CreateProductRequest request, CreateProduct useCase, CancellationToken ct) =>
            await HandleAsync(
                async () =>
                {
                    var product = await useCase.Handle(
                        new CreateProductCommand(request.Name, request.Description, request.BrandId, request.CategoryId),
                        ct);
                    return Results.Created($"/api/products/{product.Id}", ProductResponse.From(product));
                },
                ct));

        group.MapGet("/products", async (ListProducts useCase, string? search, bool includeInactive = false, CancellationToken ct = default) =>
        {
            var products = await useCase.Handle(search, includeInactive, ct);
            return Results.Ok(products.Select(ProductResponse.From));
        });

        group.MapGet("/products/{id:guid}", async (Guid id, GetProduct useCase, CancellationToken ct) =>
        {
            var product = await useCase.Handle(id, ct);
            return product is null ? Results.NotFound() : Results.Ok(ProductResponse.From(product));
        });

        group.MapPost("/skus", async (CreateSkuRequest request, CreateSku useCase, GetSku getSku, CancellationToken ct) =>
            await HandleAsync(
                async () =>
                {
                    var sku = await useCase.Handle(
                        new CreateSkuCommand(
                            request.ProductId,
                            request.Code,
                            request.Name,
                            request.Barcode,
                            request.UomCode,
                            request.WeightKg,
                            request.LengthCm,
                            request.WidthCm,
                            request.HeightCm),
                        ct);
                    var full = await getSku.Handle(sku.Id, ct);
                    return Results.Created($"/api/skus/{sku.Id}", full is null ? null : SkuResponse.From(full));
                },
                ct));

        group.MapGet("/skus", async (ListSkus useCase, Guid? productId, string? search, bool includeInactive = false, CancellationToken ct = default) =>
        {
            var skus = await useCase.Handle(productId, search, includeInactive, ct);
            return Results.Ok(skus.Select(SkuResponse.From));
        });

        group.MapGet("/skus/{id:guid}", async (Guid id, GetSku useCase, CancellationToken ct) =>
        {
            var sku = await useCase.Handle(id, ct);
            return sku is null ? Results.NotFound() : Results.Ok(SkuResponse.From(sku));
        });

        group.MapPost("/skus/{id:guid}/deactivate", async (Guid id, DeactivateSku useCase, CancellationToken ct) =>
            await HandleAsync(
                async () =>
                {
                    await useCase.Handle(id, ct);
                    return Results.NoContent();
                },
                ct));

        group.MapPost("/catalog/import", async (CatalogImportRequest request, ImportCatalog useCase, CancellationToken ct) =>
        {
            var items = request.Items.Select(i => new ProductCatalogItemInput
            {
                ExternalId = i.ExternalId,
                Name = i.Name,
                SkuName = i.SkuName,
                SkuCode = i.SkuCode,
                Barcode = i.Barcode,
                Brand = i.Brand,
                Category = i.Category,
                Uom = i.Uom,
                WeightKg = i.WeightKg,
                LengthCm = i.LengthCm,
                WidthCm = i.WidthCm,
                HeightCm = i.HeightCm,
            }).ToList();

            var result = await useCase.Handle(items, ct);
            return Results.Ok(new CatalogImportResponse(result.ProductsCreated, result.SkusCreated, result.Skipped));
        });

        group.MapPost("/catalog/seed-demo", async (ImportCatalog useCase, CancellationToken ct) =>
        {
            var result = await useCase.Handle(SyntheticCatalogFactory.CreateCatalog(), ct);
            return Results.Ok(new CatalogImportResponse(result.ProductsCreated, result.SkusCreated, result.Skipped));
        });

        return endpoints;
    }

    private static async Task<IResult> HandleAsync(Func<Task<IResult>> action, CancellationToken ct)
    {
        try
        {
            return await action();
        }
        catch (MasterDataNotFoundException exception)
        {
            return Results.NotFound(exception.Message);
        }
        catch (DuplicateSkuException exception)
        {
            return Results.Conflict(exception.Message);
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(exception.Message);
        }
    }
}
