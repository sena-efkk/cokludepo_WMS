using Wms.Modules.MasterData.Domain;

namespace Wms.Modules.MasterData.Application.Import;

public sealed class ImportCatalog(IMasterDataStore store)
{
    public async Task<CatalogImportResult> Handle(IReadOnlyList<ProductCatalogItemInput> items, CancellationToken cancellationToken)
    {
        var productsCreated = 0;
        var skusCreated = 0;
        var skipped = 0;

        var pendingProducts = new Dictionary<string, Product>(StringComparer.OrdinalIgnoreCase);
        var pendingBrands = new Dictionary<string, Brand>(StringComparer.OrdinalIgnoreCase);
        var pendingCategories = new Dictionary<string, Category>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            if (!string.IsNullOrWhiteSpace(item.Barcode)
                && await store.GetSkuByBarcodeAsync(item.Barcode.Trim(), cancellationToken) is not null)
            {
                skipped++;
                continue;
            }

            if (!string.IsNullOrWhiteSpace(item.SkuCode)
                && await store.GetSkuByCodeAsync(item.SkuCode.Trim(), cancellationToken) is not null)
            {
                skipped++;
                continue;
            }

            var (product, isNewProduct) = await GetOrCreateProductAsync(item, pendingProducts, pendingBrands, pendingCategories, cancellationToken);
            if (isNewProduct)
            {
                productsCreated++;
            }

            var uomCode = string.IsNullOrWhiteSpace(item.Uom) ? CreateSku.DefaultUomCode : item.Uom.Trim();
            var uom = await store.GetUomByCodeAsync(uomCode, cancellationToken)
                ?? throw new UomNotFoundException(uomCode);

            var code = string.IsNullOrWhiteSpace(item.SkuCode)
                ? SkuCodeGenerator.Format(await store.NextSkuSequenceAsync(cancellationToken))
                : item.SkuCode.Trim();

            var sku = Sku.Create(
                product.Id,
                code,
                uom.Id,
                item.SkuName,
                item.WeightKg,
                item.LengthCm,
                item.WidthCm,
                item.HeightCm);

            if (!string.IsNullOrWhiteSpace(item.Barcode))
            {
                sku.AddBarcode(item.Barcode.Trim(), BarcodeType.Ean);
            }

            await store.AddSkuAsync(sku, cancellationToken);
            skusCreated++;
        }

        await store.SaveChangesAsync(cancellationToken);
        return new CatalogImportResult(productsCreated, skusCreated, skipped);
    }

    private async Task<(Product Product, bool IsNew)> GetOrCreateProductAsync(
        ProductCatalogItemInput item,
        Dictionary<string, Product> pendingProducts,
        Dictionary<string, Brand> pendingBrands,
        Dictionary<string, Category> pendingCategories,
        CancellationToken cancellationToken)
    {
        var name = item.Name.Trim();
        if (pendingProducts.TryGetValue(name, out var pending))
        {
            return (pending, false);
        }

        var existing = await store.GetProductByNameAsync(name, cancellationToken);
        if (existing is not null)
        {
            pendingProducts[name] = existing;
            return (existing, false);
        }

        Brand? brand = null;
        if (!string.IsNullOrWhiteSpace(item.Brand))
        {
            brand = await GetOrCreateAsync(
                item.Brand.Trim(),
                pendingBrands,
                store.GetBrandByNameAsync,
                store.AddBrandAsync,
                Brand.Create,
                cancellationToken);
        }

        Category? category = null;
        if (!string.IsNullOrWhiteSpace(item.Category))
        {
            category = await GetOrCreateAsync(
                item.Category.Trim(),
                pendingCategories,
                store.GetCategoryByNameAsync,
                store.AddCategoryAsync,
                Category.Create,
                cancellationToken);
        }

        var product = Product.Create(name, brandId: brand?.Id, categoryId: category?.Id);
        await store.AddProductAsync(product, cancellationToken);
        pendingProducts[name] = product;
        return (product, true);
    }

    private static async Task<T> GetOrCreateAsync<T>(
        string name,
        Dictionary<string, T> pending,
        Func<string, CancellationToken, Task<T?>> find,
        Func<T, CancellationToken, Task> add,
        Func<string, T> create,
        CancellationToken cancellationToken)
        where T : class
    {
        if (pending.TryGetValue(name, out var pendingItem))
        {
            return pendingItem;
        }

        var existing = await find(name, cancellationToken);
        if (existing is not null)
        {
            pending[name] = existing;
            return existing;
        }

        var created = create(name);
        await add(created, cancellationToken);
        pending[name] = created;
        return created;
    }
}
