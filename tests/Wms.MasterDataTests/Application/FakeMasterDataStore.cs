using Wms.Modules.MasterData.Application;
using Wms.Modules.MasterData.Domain;

namespace Wms.MasterDataTests.Application;

public sealed class FakeMasterDataStore : IMasterDataStore
{
    public List<Product> Products { get; } = [];

    public List<Sku> Skus { get; } = [];

    public List<Uom> Uoms { get; } = [];

    public List<Brand> Brands { get; } = [];

    public List<Category> Categories { get; } = [];

    public long NextSequence { get; set; } = 1;

    public int SaveChangesCount { get; private set; }

    public Task<Product?> GetProductAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Products.FirstOrDefault(p => p.Id == id));

    public Task<Product?> GetProductByNameAsync(string name, CancellationToken cancellationToken) =>
        Task.FromResult(Products.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)));

    public Task<IReadOnlyList<Product>> ListProductsAsync(string? search, bool includeInactive, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Product>>(Products
            .Where(p => includeInactive || p.IsActive)
            .Where(p => search is null || p.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
            .ToList());

    public Task AddProductAsync(Product product, CancellationToken cancellationToken)
    {
        Products.Add(product);
        return Task.CompletedTask;
    }

    public Task<Sku?> GetSkuAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Skus.FirstOrDefault(s => s.Id == id));

    public Task<Sku?> GetSkuByCodeAsync(string code, CancellationToken cancellationToken) =>
        Task.FromResult(Skus.FirstOrDefault(s => s.Code == code));

    public Task<Sku?> GetSkuByBarcodeAsync(string barcode, CancellationToken cancellationToken) =>
        Task.FromResult(Skus.FirstOrDefault(s => s.Barcodes.Any(b => b.Value == barcode)));

    public Task<IReadOnlyList<Sku>> ListSkusAsync(Guid? productId, string? search, bool includeInactive, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Sku>>(Skus
            .Where(s => includeInactive || s.IsActive)
            .Where(s => productId is null || s.ProductId == productId)
            .ToList());

    public Task AddSkuAsync(Sku sku, CancellationToken cancellationToken)
    {
        Skus.Add(sku);
        return Task.CompletedTask;
    }

    public Task<Uom?> GetUomByCodeAsync(string code, CancellationToken cancellationToken) =>
        Task.FromResult(Uoms.FirstOrDefault(u => u.Code == code));

    public Task<Brand?> GetBrandByNameAsync(string name, CancellationToken cancellationToken) =>
        Task.FromResult(Brands.FirstOrDefault(b => string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase)));

    public Task<Category?> GetCategoryByNameAsync(string name, CancellationToken cancellationToken) =>
        Task.FromResult(Categories.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)));

    public Task AddBrandAsync(Brand brand, CancellationToken cancellationToken)
    {
        Brands.Add(brand);
        return Task.CompletedTask;
    }

    public Task AddCategoryAsync(Category category, CancellationToken cancellationToken)
    {
        Categories.Add(category);
        return Task.CompletedTask;
    }

    public Task<long> NextSkuSequenceAsync(CancellationToken cancellationToken) =>
        Task.FromResult(NextSequence++);

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveChangesCount++;
        return Task.CompletedTask;
    }
}
