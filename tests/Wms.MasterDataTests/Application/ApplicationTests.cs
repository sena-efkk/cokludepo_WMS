using Wms.Modules.MasterData.Application;
using Wms.Modules.MasterData.Domain;
using Xunit;

namespace Wms.MasterDataTests.Application;

public sealed class SkuCodeGeneratorTests
{
    [Theory]
    [InlineData(1, "SKU-000001")]
    [InlineData(42, "SKU-000042")]
    [InlineData(999999, "SKU-999999")]
    [InlineData(1000000, "SKU-1000000")]
    public void Format_produces_deterministic_padded_code(long sequence, string expected)
    {
        Assert.Equal(expected, SkuCodeGenerator.Format(sequence));
    }
}

public sealed class CreateSkuTests
{
    [Fact]
    public async Task Generates_code_when_code_missing()
    {
        var store = new FakeMasterDataStore { NextSequence = 7 };
        store.Products.Add(Product.Create("Kalem"));
        store.Uoms.Add(Uom.Create("EA", "Each"));
        var useCase = new CreateSku(store);

        var sku = await useCase.Handle(
            new CreateSkuCommand(store.Products[0].Id, null, null, null, null, null, null, null, null),
            CancellationToken.None);

        Assert.Equal("SKU-000007", sku.Code);
    }

    [Fact]
    public async Task Rejects_unknown_product()
    {
        var store = new FakeMasterDataStore();
        var useCase = new CreateSku(store);

        await Assert.ThrowsAsync<ProductNotFoundException>(() => useCase.Handle(
            new CreateSkuCommand(Guid.NewGuid(), "SKU-1", null, null, null, null, null, null, null),
            CancellationToken.None));
    }

    [Fact]
    public async Task Rejects_unknown_uom()
    {
        var store = new FakeMasterDataStore();
        store.Products.Add(Product.Create("Kalem"));
        var useCase = new CreateSku(store);

        await Assert.ThrowsAsync<UomNotFoundException>(() => useCase.Handle(
            new CreateSkuCommand(store.Products[0].Id, "SKU-1", null, null, "PAKET", null, null, null, null),
            CancellationToken.None));
    }

    [Fact]
    public async Task Rejects_duplicate_code()
    {
        var store = new FakeMasterDataStore();
        store.Products.Add(Product.Create("Kalem"));
        store.Uoms.Add(Uom.Create("EA", "Each"));
        store.Skus.Add(Sku.Create(store.Products[0].Id, "SKU-1", store.Uoms[0].Id));
        var useCase = new CreateSku(store);

        await Assert.ThrowsAsync<DuplicateSkuException>(() => useCase.Handle(
            new CreateSkuCommand(store.Products[0].Id, "SKU-1", null, null, null, null, null, null, null),
            CancellationToken.None));
    }

    [Fact]
    public async Task Rejects_duplicate_barcode()
    {
        var store = new FakeMasterDataStore();
        store.Products.Add(Product.Create("Kalem"));
        store.Uoms.Add(Uom.Create("EA", "Each"));
        var existing = Sku.Create(store.Products[0].Id, "SKU-1", store.Uoms[0].Id);
        existing.AddBarcode("8691234567890", BarcodeType.Ean);
        store.Skus.Add(existing);
        var useCase = new CreateSku(store);

        await Assert.ThrowsAsync<DuplicateSkuException>(() => useCase.Handle(
            new CreateSkuCommand(store.Products[0].Id, "SKU-2", null, "8691234567890", null, null, null, null, null),
            CancellationToken.None));
    }

    [Fact]
    public async Task Uses_default_uom_ea_when_uom_missing()
    {
        var store = new FakeMasterDataStore();
        store.Products.Add(Product.Create("Kalem"));
        store.Uoms.Add(Uom.Create("EA", "Each"));
        var useCase = new CreateSku(store);

        var sku = await useCase.Handle(
            new CreateSkuCommand(store.Products[0].Id, "SKU-9", null, null, null, null, null, null, null),
            CancellationToken.None);

        Assert.Equal(store.Uoms[0].Id, sku.UomId);
    }
}

public sealed class DeactivateSkuTests
{
    [Fact]
    public async Task Deactivates_active_sku()
    {
        var store = new FakeMasterDataStore();
        store.Products.Add(Product.Create("Kalem"));
        store.Uoms.Add(Uom.Create("EA", "Each"));
        var sku = Sku.Create(store.Products[0].Id, "SKU-1", store.Uoms[0].Id);
        store.Skus.Add(sku);
        var useCase = new DeactivateSku(store);

        await useCase.Handle(sku.Id, CancellationToken.None);

        Assert.False(sku.IsActive);
    }

    [Fact]
    public async Task Is_idempotent_when_already_inactive()
    {
        var store = new FakeMasterDataStore();
        store.Products.Add(Product.Create("Kalem"));
        store.Uoms.Add(Uom.Create("EA", "Each"));
        var sku = Sku.Create(store.Products[0].Id, "SKU-1", store.Uoms[0].Id);
        sku.Deactivate();
        store.Skus.Add(sku);
        var useCase = new DeactivateSku(store);

        await useCase.Handle(sku.Id, CancellationToken.None);

        Assert.Equal(0, store.SaveChangesCount);
    }

    [Fact]
    public async Task Throws_when_sku_missing()
    {
        var store = new FakeMasterDataStore();
        var useCase = new DeactivateSku(store);

        await Assert.ThrowsAsync<SkuNotFoundException>(() => useCase.Handle(Guid.NewGuid(), CancellationToken.None));
    }
}
