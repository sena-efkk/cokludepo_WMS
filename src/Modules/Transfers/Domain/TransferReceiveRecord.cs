namespace Wms.Modules.Transfers.Domain;

public sealed class TransferReceiveRecord
{
    private TransferReceiveRecord()
    {
        InventoryStatus = string.Empty;
    }

    private TransferReceiveRecord(
        Guid requestId,
        Guid transferLineId,
        int quantity,
        Guid receivingLocationId,
        string inventoryStatus,
        DateTime receivedAt)
    {
        Id = Guid.NewGuid();
        RequestId = requestId;
        TransferLineId = transferLineId;
        Quantity = quantity;
        ReceivingLocationId = receivingLocationId;
        InventoryStatus = inventoryStatus;
        ReceivedAt = receivedAt;
    }

    public Guid Id { get; private set; }

    public Guid RequestId { get; private set; }

    public Guid TransferLineId { get; private set; }

    public int Quantity { get; private set; }

    public Guid ReceivingLocationId { get; private set; }

    public string InventoryStatus { get; private set; }

    public DateTime ReceivedAt { get; private set; }

    public static TransferReceiveRecord Create(
        Guid requestId,
        Guid transferLineId,
        int quantity,
        Guid receivingLocationId,
        string inventoryStatus,
        DateTime? receivedAt = null)
    {
        if (requestId == Guid.Empty || transferLineId == Guid.Empty)
        {
            throw new ArgumentException("Receive record RequestId ve transfer line zorunludur.");
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

        return new TransferReceiveRecord(
            requestId,
            transferLineId,
            quantity,
            receivingLocationId,
            inventoryStatus.Trim().ToUpperInvariant(),
            receivedAt ?? DateTime.UtcNow);
    }
}
