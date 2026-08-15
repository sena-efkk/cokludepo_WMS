namespace Wms.Modules.Inbound.Domain;

public sealed record ReceiptLineSpec(Guid SkuId, int ExpectedQuantity);

public sealed class InboundReceipt : IHasTimestamps
{
    private readonly List<InboundReceiptLine> _lines = [];

    private InboundReceipt()
    {
        ReceiptNumber = string.Empty;
    }

    private InboundReceipt(
        Guid requestId,
        string receiptNumber,
        Guid warehouseId,
        string? externalReference,
        string? sourceType)
    {
        Id = Guid.NewGuid();
        RequestId = requestId;
        ReceiptNumber = receiptNumber;
        WarehouseId = warehouseId;
        ExternalReference = externalReference;
        SourceType = sourceType;
        Status = ReceiptStatus.Open;
        var now = DateTime.UtcNow;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid RequestId { get; private set; }

    public string ReceiptNumber { get; private set; }

    public Guid WarehouseId { get; private set; }

    public string? ExternalReference { get; private set; }

    public string? SourceType { get; private set; }

    public ReceiptStatus Status { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime? ReceivingStartedAt { get; private set; }

    public DateTime? CompletedAt { get; private set; }

    public DateTime? CancelledAt { get; private set; }

    public DateTime UpdatedAt { get; set; }

    public IReadOnlyCollection<InboundReceiptLine> Lines => _lines;

    public static InboundReceipt Create(
        Guid requestId,
        string receiptNumber,
        Guid warehouseId,
        string? externalReference,
        string? sourceType,
        IReadOnlyList<ReceiptLineSpec> lineSpecs)
    {
        if (requestId == Guid.Empty)
        {
            throw new ArgumentException("Receipt bir RequestId taşımalıdır.", nameof(requestId));
        }

        if (string.IsNullOrWhiteSpace(receiptNumber))
        {
            throw new ArgumentException("Receipt number boş olamaz.", nameof(receiptNumber));
        }

        if (warehouseId == Guid.Empty)
        {
            throw new ArgumentException("Receipt bir warehouse'a bağlı olmalıdır.", nameof(warehouseId));
        }

        if (lineSpecs.Count == 0)
        {
            throw new ArgumentException("Receipt en az bir line içermelidir.", nameof(lineSpecs));
        }

        var receipt = new InboundReceipt(
            requestId,
            receiptNumber.Trim().ToUpperInvariant(),
            warehouseId,
            string.IsNullOrWhiteSpace(externalReference) ? null : externalReference.Trim(),
            string.IsNullOrWhiteSpace(sourceType) ? null : sourceType.Trim());

        foreach (var spec in lineSpecs)
        {
            receipt._lines.Add(InboundReceiptLine.Create(receipt.Id, spec.SkuId, spec.ExpectedQuantity));
        }

        return receipt;
    }

    public InboundReceiptLine GetLine(Guid lineId) =>
        _lines.FirstOrDefault(l => l.Id == lineId)
        ?? throw new InvalidOperationException($"Receipt line bulunamadı: {lineId}");

    public void RegisterReceive(Guid lineId, int quantity, ReceivingDisposition disposition, DateTime receivedAt)
    {
        if (Status is ReceiptStatus.Cancelled or ReceiptStatus.Completed or ReceiptStatus.Received or ReceiptStatus.PutawayInProgress)
        {
            throw new InvalidOperationException($"Receipt {Status} durumundayken receive yapılamaz.");
        }

        var line = GetLine(lineId);
        line.AddReceived(quantity, disposition);
        ReceivingStartedAt ??= receivedAt;
        Status = _lines.All(l => l.ReceivedQuantity >= l.ExpectedQuantity)
            ? ReceiptStatus.Received
            : ReceiptStatus.PartiallyReceived;
    }

    public void OnPutawayTaskStarted()
    {
        if (Status == ReceiptStatus.Received)
        {
            Status = ReceiptStatus.PutawayInProgress;
        }
    }

    public void OnPutawayTaskCompleted(bool allTasksCompleted, DateTime? at = null)
    {
        if (Status == ReceiptStatus.Completed)
        {
            return;
        }

        if (allTasksCompleted)
        {
            Status = ReceiptStatus.Completed;
            CompletedAt = at ?? DateTime.UtcNow;
        }
        else if (Status == ReceiptStatus.Received)
        {
            Status = ReceiptStatus.PutawayInProgress;
        }
    }

    public void Cancel(DateTime? at = null)
    {
        if (Status != ReceiptStatus.Open)
        {
            throw new InvalidOperationException($"Yalnızca OPEN receipt iptal edilebilir. Mevcut: {Status}");
        }

        if (_lines.Any(l => l.ReceivedQuantity > 0))
        {
            throw new InvalidOperationException(
                "Fiziksel receive yapılmış receipt iptal edilemez — stok sisteme girmiştir; explicit inventory correction gerekir.");
        }

        Status = ReceiptStatus.Cancelled;
        CancelledAt = at ?? DateTime.UtcNow;
    }
}
