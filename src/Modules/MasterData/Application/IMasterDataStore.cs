using Wms.Modules.MasterData.Domain;

namespace Wms.Modules.MasterData.Application;

public interface IMasterDataStore
{
    Task<Product?> GetProductAsync(Guid id, CancellationToken cancellationToken);

    Task<Product?> GetProductByNameAsync(string name, CancellationToken cancellationToken);

    Task<IReadOnlyList<Product>> ListProductsAsync(string? search, bool includeInactive, CancellationToken cancellationToken);

    Task AddProductAsync(Product product, CancellationToken cancellationToken);

    Task<Sku?> GetSkuAsync(Guid id, CancellationToken cancellationToken);

    Task<Sku?> GetSkuByCodeAsync(string code, CancellationToken cancellationToken);

    Task<Sku?> GetSkuByBarcodeAsync(string barcode, CancellationToken cancellationToken);

    Task<IReadOnlyList<Sku>> ListSkusAsync(Guid? productId, string? search, bool includeInactive, CancellationToken cancellationToken);

    Task AddSkuAsync(Sku sku, CancellationToken cancellationToken);

    Task<Uom?> GetUomByCodeAsync(string code, CancellationToken cancellationToken);

    Task<Brand?> GetBrandByNameAsync(string name, CancellationToken cancellationToken);

    Task<Category?> GetCategoryByNameAsync(string name, CancellationToken cancellationToken);

    Task AddBrandAsync(Brand brand, CancellationToken cancellationToken);

    Task AddCategoryAsync(Category category, CancellationToken cancellationToken);

    Task<long> NextSkuSequenceAsync(CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
