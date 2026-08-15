using Wms.Modules.Transfers.Application;
using Wms.Modules.Transfers.Domain;

namespace Wms.Modules.Transfers.Application;

public sealed record TransfersSummary(
    int OpenTransfers,
    int InTransitTotal,
    int ReceivingTransfers);

public sealed class GetTransfersSummary(ITransferStore store)
{
    public async Task<TransfersSummary> Handle(Guid? warehouseId, CancellationToken cancellationToken)
    {
        var transfers = await store.ListTransfersAsync(warehouseId, 10_000, cancellationToken);
        var open = transfers.Count(t => t.Status is TransferStatus.Created or TransferStatus.Allocated);
        var inTransit = transfers.Sum(t => t.InTransitQuantity);
        var receiving = transfers.Count(t => t.Status == TransferStatus.Receiving);

        return new TransfersSummary(open, inTransit, receiving);
    }
}
