namespace Wms.Modules.MasterData.Application;

public sealed record ProductCatalogItemInput
{
    public string? ExternalId { get; init; }

    public required string Name { get; init; }

    public string? SkuName { get; init; }

    public string? SkuCode { get; init; }

    public string? Barcode { get; init; }

    public string? Brand { get; init; }

    public string? Category { get; init; }

    public string? Uom { get; init; }

    public decimal? WeightKg { get; init; }

    public decimal? LengthCm { get; init; }

    public decimal? WidthCm { get; init; }

    public decimal? HeightCm { get; init; }
}

public sealed record CatalogImportResult(int ProductsCreated, int SkusCreated, int Skipped);
