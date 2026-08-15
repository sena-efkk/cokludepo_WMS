namespace Wms.Modules.Transfers.Domain;

public sealed class TransferDiscrepancy
{
    private TransferDiscrepancy()
    {
        Reason = TransferDiscrepancyReason.Other;
    }

    private TransferDiscrepancy(
        Guid requestId,
        Guid transferLineId,
        int quantity,
        TransferDiscrepancyReason reason,
        string? note,
        DateTime createdAt)
    {
        Id = Guid.NewGuid();
        RequestId = requestId;
        TransferLineId = transferLineId;
        Quantity = quantity;
        Reason = reason;
        Note = note;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid RequestId { get; private set; }

    public Guid TransferLineId { get; private set; }

    public int Quantity { get; private set; }

    public TransferDiscrepancyReason Reason { get; private set; }

    public string? Note { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public static TransferDiscrepancy Create(
        Guid requestId,
        Guid transferLineId,
        int quantity,
        TransferDiscrepancyReason reason,
        string? note = null,
        DateTime? createdAt = null)
    {
        if (requestId == Guid.Empty)
        {
            throw new ArgumentException("Discrepancy bir RequestId taşımalıdır.", nameof(requestId));
        }

        if (transferLineId == Guid.Empty)
        {
            throw new ArgumentException("Discrepancy bir transfer line'a bağlı olmalıdır.", nameof(transferLineId));
        }

        if (quantity <= 0)
        {
            throw new ArgumentException("Discrepancy quantity pozitif olmalıdır.", nameof(quantity));
        }

        return new TransferDiscrepancy(
            requestId,
            transferLineId,
            quantity,
            reason,
            string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            createdAt ?? DateTime.UtcNow);
    }
}
