using Wms.Modules.MasterData.Domain;

namespace Wms.Api.MasterData;

public sealed record CreateProductRequest(string Name, string? Description, Guid? BrandId, Guid? CategoryId);

public sealed record ProductResponse(Guid Id, string Name, string? Description, Guid? BrandId, Guid? CategoryId, bool IsActive)
{
    public static ProductResponse From(Product product) =>
        new(product.Id, product.Name, product.Description, product.BrandId, product.CategoryId, product.IsActive);
}

public sealed record CreateSkuRequest(
    Guid ProductId,
    string? Code,
    string? Name,
    string? Barcode,
    string? UomCode,
    decimal? WeightKg,
    decimal? LengthCm,
    decimal? WidthCm,
    decimal? HeightCm);

public sealed record SkuBarcodeResponse(string Value, string Type);

public sealed record SkuResponse(
    Guid Id,
    Guid ProductId,
    string ProductName,
    string Code,
    string? Name,
    IReadOnlyList<SkuBarcodeResponse> Barcodes,
    string UomCode,
    decimal? WeightKg,
    decimal? LengthCm,
    decimal? WidthCm,
    decimal? HeightCm,
    bool IsActive)
{
    public static SkuResponse From(Sku sku) =>
        new(
            sku.Id,
            sku.ProductId,
            sku.Product?.Name ?? string.Empty,
            sku.Code,
            sku.Name,
            sku.Barcodes.Select(b => new SkuBarcodeResponse(b.Value, b.Type.ToString())).ToList(),
            sku.Uom?.Code ?? string.Empty,
            sku.WeightKg,
            sku.LengthCm,
            sku.WidthCm,
            sku.HeightCm,
            sku.IsActive);
}

public sealed record CatalogItemRequest(
    string? ExternalId,
    string Name,
    string? SkuName,
    string? SkuCode,
    string? Barcode,
    string? Brand,
    string? Category,
    string? Uom,
    decimal? WeightKg,
    decimal? LengthCm,
    decimal? WidthCm,
    decimal? HeightCm);

public sealed record CatalogImportRequest(IReadOnlyList<CatalogItemRequest> Items);

public sealed record CatalogImportResponse(int ProductsCreated, int SkusCreated, int Skipped);
