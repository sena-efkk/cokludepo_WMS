namespace Wms.Modules.Inbound.Domain;

public sealed class ReceiptLineReceiveRecord
{
    private ReceiptLineReceiveRecord()
    {
        InventoryStatus = string.Empty;
    }

    private ReceiptLineReceiveRecord(
        Guid requestId,
        Guid receiptLineId,
        int quantity,
        ReceivingDisposition disposition,
        Guid receivingLocationId,
        string inventoryStatus,
        Guid inventoryOperationId,
        DateTime receivedAt)
    {
        Id = Guid.NewGuid();
        RequestId = requestId;
        ReceiptLineId = receiptLineId;
        Quantity = quantity;
        Disposition = disposition;
        ReceivingLocationId = receivingLocationId;
        InventoryStatus = inventoryStatus;
        InventoryOperationId = inventoryOperationId;
        ReceivedAt = receivedAt;
    }

    public Guid Id { get; private set; }

    public Guid RequestId { get; private set; }

    public Guid ReceiptLineId { get; private set; }

    public int Quantity { get; private set; }

    public ReceivingDisposition Disposition { get; private set; }

    public Guid ReceivingLocationId { get; private set; }

    public string InventoryStatus { get; private set; }

    public Guid InventoryOperationId { get; private set; }

    public DateTime ReceivedAt { get; private set; }

    public static ReceiptLineReceiveRecord Create(
        Guid requestId,
        Guid receiptLineId,
        int quantity,
        ReceivingDisposition disposition,
        Guid receivingLocationId,
        string inventoryStatus,
        Guid inventoryOperationId,
        DateTime? receivedAt = null)
    {
        if (requestId == Guid.Empty)
        {
            throw new ArgumentException("Receive record bir RequestId taşımalıdır.", nameof(requestId));
        }

        if (receiptLineId == Guid.Empty)
        {
            throw new ArgumentException("Receive record bir receipt line'a bağlı olmalıdır.", nameof(receiptLineId));
        }

        if (quantity <= 0)
        {
            throw new ArgumentException("Receive record quantity pozitif olmalıdır.", nameof(quantity));
        }

        if (receivingLocationId == Guid.Empty)
        {
            throw new ArgumentException("Receive record bir receiving location taşımalıdır.", nameof(receivingLocationId));
        }

        if (string.IsNullOrWhiteSpace(inventoryStatus))
        {
            throw new ArgumentException("Receive record bir inventory status taşımalıdır.", nameof(inventoryStatus));
        }

        if (inventoryOperationId == Guid.Empty)
        {
            throw new ArgumentException("Receive record bir inventory operation'a bağlı olmalıdır.", nameof(inventoryOperationId));
        }

        return new ReceiptLineReceiveRecord(
            requestId,
            receiptLineId,
            quantity,
            disposition,
            receivingLocationId,
            inventoryStatus.Trim().ToUpperInvariant(),
            inventoryOperationId,
            receivedAt ?? DateTime.UtcNow);
    }
}
