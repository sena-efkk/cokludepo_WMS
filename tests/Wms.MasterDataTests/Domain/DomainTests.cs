using Wms.Modules.MasterData.Domain;
using Xunit;

namespace Wms.MasterDataTests.Domain;

public sealed class SkuDomainTests
{
    private static readonly Guid ProductId = Guid.NewGuid();
    private static readonly Guid UomId = Guid.NewGuid();

    [Fact]
    public void Create_rejects_empty_code()
    {
        Assert.Throws<ArgumentException>(() => Sku.Create(ProductId, " ", UomId));
    }

    [Fact]
    public void Create_rejects_empty_product_id()
    {
        Assert.Throws<ArgumentException>(() => Sku.Create(Guid.Empty, "SKU-1", UomId));
    }

    [Fact]
    public void Create_rejects_empty_uom_id()
    {
        Assert.Throws<ArgumentException>(() => Sku.Create(ProductId, "SKU-1", Guid.Empty));
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(-5)]
    public void Create_rejects_negative_measurements(decimal negativeValue)
    {
        Assert.Throws<ArgumentException>(() =>
            Sku.Create(ProductId, "SKU-1", UomId, weightKg: negativeValue));

        Assert.Throws<ArgumentException>(() =>
            Sku.Create(ProductId, "SKU-1", UomId, lengthCm: negativeValue));
    }

    [Fact]
    public void Create_trims_code()
    {
        var sku = Sku.Create(ProductId, "  SKU-42  ", UomId);
        Assert.Equal("SKU-42", sku.Code);
    }

    [Fact]
    public void Deactivate_sets_inactive()
    {
        var sku = Sku.Create(ProductId, "SKU-1", UomId);
        Assert.True(sku.IsActive);

        sku.Deactivate();

        Assert.False(sku.IsActive);
    }

    [Fact]
    public void AddBarcode_adds_unique_barcode()
    {
        var sku = Sku.Create(ProductId, "SKU-1", UomId);

        sku.AddBarcode("8691234567890", BarcodeType.Ean);
        sku.AddBarcode("8690000000000", BarcodeType.Supplier);

        Assert.Equal(2, sku.Barcodes.Count);
    }

    [Fact]
    public void AddBarcode_rejects_duplicate_value_in_same_sku()
    {
        var sku = Sku.Create(ProductId, "SKU-1", UomId);
        sku.AddBarcode("8691234567890", BarcodeType.Ean);

        Assert.Throws<ArgumentException>(() => sku.AddBarcode("8691234567890", BarcodeType.Supplier));
    }

    [Fact]
    public void AddBarcode_rejects_empty_value()
    {
        var sku = Sku.Create(ProductId, "SKU-1", UomId);

        Assert.Throws<ArgumentException>(() => sku.AddBarcode(" ", BarcodeType.Ean));
    }
}

public sealed class ProductDomainTests
{
    [Fact]
    public void Create_rejects_empty_name()
    {
        Assert.Throws<ArgumentException>(() => Product.Create(" "));
    }

    [Fact]
    public void Create_trims_name_and_starts_active()
    {
        var product = Product.Create("  Basic T-Shirt  ");

        Assert.Equal("Basic T-Shirt", product.Name);
        Assert.True(product.IsActive);
    }

    [Fact]
    public void Deactivate_sets_inactive()
    {
        var product = Product.Create("Basic T-Shirt");
        product.Deactivate();

        Assert.False(product.IsActive);
    }
}
