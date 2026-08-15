namespace Wms.Modules.Transfers.Contracts;

public interface ITransferContract
{
    Task<int> GetOpenInTransitTotalAsync(CancellationToken cancellationToken);

    Task<int> GetOpenInTransitBySkuAsync(Guid skuId, CancellationToken cancellationToken);
}
