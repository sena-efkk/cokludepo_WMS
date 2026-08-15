using Wms.Modules.Inbound.Domain;

namespace Wms.Modules.Inbound.Application;

public sealed class CancelReceipt(IInboundStore store)
{
    public async Task<InboundReceipt> Handle(Guid receiptId, CancellationToken cancellationToken)
    {
        var receipt = await store.GetReceiptAsync(receiptId, cancellationToken)
            ?? throw new ReceiptNotFoundException(receiptId);

        if (receipt.Status == ReceiptStatus.Cancelled)
        {
            return receipt;
        }

        if (receipt.Status != ReceiptStatus.Open)
        {
            throw new InvalidReceiptStateException(
                $"Yalnızca OPEN receipt iptal edilebilir. Mevcut: {receipt.Status} — fiziksel receive yapılmışsa explicit inventory correction gerekir.");
        }

        receipt.Cancel();
        await store.SaveChangesAsync(cancellationToken);
        return receipt;
    }
}
