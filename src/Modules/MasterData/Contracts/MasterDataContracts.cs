namespace Wms.Modules.MasterData.Contracts;

public sealed record SkuInfo(Guid Id, string Code, bool IsActive);

public interface IMasterDataQueryContract
{
    Task<SkuInfo?> GetSkuAsync(Guid skuId, CancellationToken cancellationToken);

    Task<SkuInfo?> GetSkuByBarcodeAsync(string barcode, CancellationToken cancellationToken);

    Task<IReadOnlyList<SkuInfo>> GetSkusByIdsAsync(IReadOnlyList<Guid> skuIds, CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>> SearchSkuIdsAsync(string query, int limit, CancellationToken cancellationToken);
}
