using Wms.Modules.MasterData.Application.Import;
using Xunit;

namespace Wms.MasterDataTests.Synthetic;

public sealed class SyntheticCatalogTests
{
    [Fact]
    public void Catalog_is_deterministic()
    {
        var first = SyntheticCatalogFactory.CreateCatalog();
        var second = SyntheticCatalogFactory.CreateCatalog();

        Assert.Equal(first.Count, second.Count);
        Assert.Equal(
            first.Select(i => i.Barcode).OrderBy(b => b, StringComparer.Ordinal),
            second.Select(i => i.Barcode).OrderBy(b => b, StringComparer.Ordinal));
    }

    [Fact]
    public void Catalog_has_expected_size_and_shape()
    {
        var catalog = SyntheticCatalogFactory.CreateCatalog();

        Assert.InRange(catalog.Count, 30, 100);
        Assert.All(catalog, item => Assert.False(string.IsNullOrWhiteSpace(item.Name)));
        Assert.All(catalog, item => Assert.False(string.IsNullOrWhiteSpace(item.Barcode)));
        Assert.All(catalog, item => Assert.False(string.IsNullOrWhiteSpace(item.Brand)));
        Assert.All(catalog, item => Assert.False(string.IsNullOrWhiteSpace(item.Category)));
    }

    [Fact]
    public void Catalog_barcodes_are_unique()
    {
        var catalog = SyntheticCatalogFactory.CreateCatalog();

        var duplicates = catalog.GroupBy(i => i.Barcode).Where(g => g.Count() > 1).ToList();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void Catalog_covers_all_five_categories()
    {
        var categories = SyntheticCatalogFactory.CreateCatalog()
            .Select(i => i.Category)
            .Distinct()
            .ToList();

        Assert.Equivalent(
            new[] { "Kırtasiye", "Tekstil", "Ev Yaşam", "Kozmetik", "Elektronik Aksesuar" },
            categories);
    }
}
