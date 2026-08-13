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
}
