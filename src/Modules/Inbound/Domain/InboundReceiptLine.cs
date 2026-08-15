namespace Wms.Modules.Inbound.Domain;

public sealed class InboundReceiptLine : IHasTimestamps
{
    private InboundReceiptLine()
    {
    }

    private InboundReceiptLine(Guid receiptId, Guid skuId, int expectedQuantity)
    {
        Id = Guid.NewGuid();
        ReceiptId = receiptId;
        SkuId = skuId;
        ExpectedQuantity = expectedQuantity;
        ReceivedQuantity = 0;
        Disposition = null;
        var now = DateTime.UtcNow;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid ReceiptId { get; private set; }

    public Guid SkuId { get; private set; }

    public int ExpectedQuantity { get; private set; }

    public int ReceivedQuantity { get; private set; }

    public ReceivingDisposition? Disposition { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime UpdatedAt { get; set; }

    public static InboundReceiptLine Create(Guid receiptId, Guid skuId, int expectedQuantity)
    {
        if (receiptId == Guid.Empty)
        {
            throw new ArgumentException("Receipt line bir receipt'a bağlı olmalıdır.", nameof(receiptId));
        }

        if (skuId == Guid.Empty)
        {
            throw new ArgumentException("Receipt line bir SKU'ya bağlı olmalıdır.", nameof(skuId));
        }

        if (expectedQuantity <= 0)
        {
            throw new ArgumentException("Expected quantity pozitif olmalıdır.", nameof(expectedQuantity));
        }

        return new InboundReceiptLine(receiptId, skuId, expectedQuantity);
    }

    public void AddReceived(int quantity, ReceivingDisposition disposition)
    {
        if (quantity <= 0)
        {
            throw new ArgumentException("Received quantity pozitif olmalıdır.", nameof(quantity));
        }

        ReceivedQuantity += quantity;
        Disposition = disposition;
    }
}
