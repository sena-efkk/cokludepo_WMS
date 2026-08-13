using Microsoft.EntityFrameworkCore;
using Npgsql.EntityFrameworkCore.PostgreSQL;
using Wms.Modules.MasterData.Application;
using Wms.Modules.MasterData.Domain;

namespace Wms.Modules.MasterData.Infrastructure.Persistence;

public sealed class MasterDataStore(MasterDataDbContext db) : IMasterDataStore
{
    public async Task<Product?> GetProductAsync(Guid id, CancellationToken cancellationToken)
    {
        return await db.Products.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    public async Task<Product?> GetProductByNameAsync(string name, CancellationToken cancellationToken)
    {
        return await db.Products.FirstOrDefaultAsync(p => p.Name == name, cancellationToken);
    }

    public async Task<IReadOnlyList<Product>> ListProductsAsync(string? search, bool includeInactive, CancellationToken cancellationToken)
    {
        var query = db.Products.AsNoTracking().AsQueryable();

        if (!includeInactive)
        {
            query = query.Where(p => p.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search.Trim()}%";
            query = query.Where(p =>
                EF.Functions.ILike(p.Name, term)
                || (p.Description != null && EF.Functions.ILike(p.Description, term)));
        }

        var result = await query.OrderBy(p => p.Name).ToListAsync(cancellationToken);
        return result.AsReadOnly();
    }

    public async Task AddProductAsync(Product product, CancellationToken cancellationToken)
    {
        await db.Products.AddAsync(product, cancellationToken);
    }

    public async Task<Sku?> GetSkuAsync(Guid id, CancellationToken cancellationToken)
    {
        return await db.Skus
            .Include(s => s.Product)
            .Include(s => s.Uom)
            .Include(s => s.Barcodes)
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
    }

    public async Task<Sku?> GetSkuByCodeAsync(string code, CancellationToken cancellationToken)
    {
        return await db.Skus.FirstOrDefaultAsync(s => s.Code == code, cancellationToken);
    }

    public async Task<Sku?> GetSkuByBarcodeAsync(string barcode, CancellationToken cancellationToken)
    {
        return await db.Skus.FirstOrDefaultAsync(
            s => s.Barcodes.Any(b => b.Value == barcode),
            cancellationToken);
    }

    public async Task<IReadOnlyList<Sku>> ListSkusAsync(Guid? productId, string? search, bool includeInactive, CancellationToken cancellationToken)
    {
        var query = db.Skus
            .AsNoTracking()
            .Include(s => s.Product)
            .Include(s => s.Uom)
            .Include(s => s.Barcodes)
            .AsQueryable();

        if (productId.HasValue)
        {
            query = query.Where(s => s.ProductId == productId.Value);
        }

        if (!includeInactive)
        {
            query = query.Where(s => s.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search.Trim()}%";
            query = query.Where(s =>
                EF.Functions.ILike(s.Code, term)
                || (s.Name != null && EF.Functions.ILike(s.Name, term))
                || s.Barcodes.Any(b => EF.Functions.ILike(b.Value, term))
                || EF.Functions.ILike(s.Product!.Name, term));
        }

        var result = await query.OrderBy(s => s.Code).ToListAsync(cancellationToken);
        return result.AsReadOnly();
    }

    public async Task AddSkuAsync(Sku sku, CancellationToken cancellationToken)
    {
        await db.Skus.AddAsync(sku, cancellationToken);
    }

    public async Task<Uom?> GetUomByCodeAsync(string code, CancellationToken cancellationToken)
    {
        return await db.Uoms.FirstOrDefaultAsync(u => u.Code == code, cancellationToken);
    }

    public async Task<Brand?> GetBrandByNameAsync(string name, CancellationToken cancellationToken)
    {
        return await db.Brands.FirstOrDefaultAsync(b => b.Name == name, cancellationToken);
    }

    public async Task<Category?> GetCategoryByNameAsync(string name, CancellationToken cancellationToken)
    {
        return await db.Categories.FirstOrDefaultAsync(c => c.Name == name, cancellationToken);
    }

    public async Task AddBrandAsync(Brand brand, CancellationToken cancellationToken)
    {
        await db.Brands.AddAsync(brand, cancellationToken);
    }

    public async Task AddCategoryAsync(Category category, CancellationToken cancellationToken)
    {
        await db.Categories.AddAsync(category, cancellationToken);
    }

    public async Task<long> NextSkuSequenceAsync(CancellationToken cancellationToken)
    {
        return await db.Database
            .SqlQueryRaw<long>("SELECT nextval('master_data.sku_code_seq') AS \"Value\"")
            .FirstAsync(cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return db.SaveChangesAsync(cancellationToken);
    }
}
