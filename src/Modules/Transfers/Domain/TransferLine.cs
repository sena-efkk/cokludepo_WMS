namespace Wms.Modules.Transfers.Domain;

public sealed class TransferLine : IHasTimestamps
{
    private TransferLine()
    {
    }

    private TransferLine(Guid transferOrderId, Guid skuId, int requestedQuantity)
    {
        Id = Guid.NewGuid();
        TransferOrderId = transferOrderId;
        SkuId = skuId;
        RequestedQuantity = requestedQuantity;
        ShippedQuantity = 0;
        ReceivedQuantity = 0;
        ConfirmedVarianceQuantity = 0;
        var now = DateTime.UtcNow;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid TransferOrderId { get; private set; }

    public Guid SkuId { get; private set; }

    public int RequestedQuantity { get; private set; }

    public int ShippedQuantity { get; private set; }

    public int ReceivedQuantity { get; private set; }

    public int ConfirmedVarianceQuantity { get; private set; }

    public Guid? OutboundOrderLineId { get; private set; }

    public Guid? InboundReceiptLineId { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime UpdatedAt { get; set; }

    public int InTransitQuantity => ShippedQuantity - ReceivedQuantity - ConfirmedVarianceQuantity;

    public bool IsClosed => InTransitQuantity == 0 && ShippedQuantity > 0;

    public static TransferLine Create(Guid transferOrderId, Guid skuId, int requestedQuantity)
    {
        if (transferOrderId == Guid.Empty)
        {
            throw new ArgumentException("Transfer line bir transfer'a bağlı olmalıdır.", nameof(transferOrderId));
        }

        if (skuId == Guid.Empty)
        {
            throw new ArgumentException("Transfer line bir SKU'ya bağlı olmalıdır.", nameof(skuId));
        }

        if (requestedQuantity <= 0)
        {
            throw new ArgumentException("Requested quantity pozitif olmalıdır.", nameof(requestedQuantity));
        }

        return new TransferLine(transferOrderId, skuId, requestedQuantity);
    }

    public void MarkShipped(int quantity)
    {
        if (ShippedQuantity > 0)
        {
            throw new InvalidOperationException("Transfer line zaten ship edilmiş.");
        }

        if (quantity != RequestedQuantity)
        {
            throw new InvalidOperationException(
                $"MVP ship tam miktar gerektirir: requested {RequestedQuantity}, attempt {quantity}.");
        }

        ShippedQuantity = quantity;
    }

    public void SetOutboundOrderLine(Guid outboundOrderLineId)
    {
        if (outboundOrderLineId == Guid.Empty)
        {
            throw new ArgumentException("Outbound order line id boş olamaz.", nameof(outboundOrderLineId));
        }

        OutboundOrderLineId = outboundOrderLineId;
    }

    public void SetInboundReceiptLine(Guid inboundReceiptLineId)
    {
        if (inboundReceiptLineId == Guid.Empty)
        {
            throw new ArgumentException("Inbound receipt line id boş olamaz.", nameof(inboundReceiptLineId));
        }

        InboundReceiptLineId = inboundReceiptLineId;
    }

    public void Receive(int quantity)
    {
        if (quantity <= 0)
        {
            throw new ArgumentException("Receive quantity pozitif olmalıdır.", nameof(quantity));
        }

        if (ReceivedQuantity + quantity > ShippedQuantity)
        {
            throw new InvalidOperationException(
                $"Over receipt kabul edilmez: shipped {ShippedQuantity}, received {ReceivedQuantity}, attempt {quantity} — explicit discrepancy/reconciliation gerekir.");
        }

        ReceivedQuantity += quantity;
    }

    public void ConfirmVariance(int quantity)
    {
        if (quantity <= 0)
        {
            throw new ArgumentException("Variance quantity pozitif olmalıdır.", nameof(quantity));
        }

        if (quantity > InTransitQuantity)
        {
            throw new InvalidOperationException(
                $"Variance açık InTransit'i aşamaz: InTransit {InTransitQuantity}, attempt {quantity}.");
        }

        ConfirmedVarianceQuantity += quantity;
    }
}
