using Microsoft.EntityFrameworkCore;
using Wms.Modules.MasterData.Contracts;

namespace Wms.Modules.MasterData.Infrastructure.Persistence;

public sealed class MasterDataQueryContract(MasterDataDbContext db) : IMasterDataQueryContract
{
    public async Task<SkuInfo?> GetSkuAsync(Guid skuId, CancellationToken cancellationToken)
    {
        return await db.Skus
            .Where(s => s.Id == skuId)
            .Select(s => new SkuInfo(s.Id, s.Code, s.IsActive))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<SkuInfo?> GetSkuByBarcodeAsync(string barcode, CancellationToken cancellationToken)
    {
        return await db.Skus
            .Where(s => s.Barcodes.Any(b => b.Value == barcode))
            .Select(s => new SkuInfo(s.Id, s.Code, s.IsActive))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SkuInfo>> GetSkusByIdsAsync(IReadOnlyList<Guid> skuIds, CancellationToken cancellationToken)
    {
        if (skuIds.Count == 0)
        {
            return [];
        }

        var result = await db.Skus
            .Where(s => skuIds.Contains(s.Id))
            .Select(s => new SkuInfo(s.Id, s.Code, s.IsActive))
            .ToListAsync(cancellationToken);
        return result.AsReadOnly();
    }

    public async Task<IReadOnlyList<Guid>> SearchSkuIdsAsync(string query, int limit, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var term = query.Trim();
        var result = await db.Skus
            .Where(s => s.Code.ToLower().Contains(term.ToLower())
                        || s.Barcodes.Any(b => b.Value.Contains(term))
                        || (s.Name != null && s.Name.ToLower().Contains(term.ToLower()))
                        || (s.Product != null && s.Product.Name.ToLower().Contains(term.ToLower())))
            .OrderBy(s => s.Code)
            .Take(limit)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);
        return result.AsReadOnly();
    }
}
