namespace Wms.Modules.Transfers.Domain;

public sealed record TransferLineSpec(Guid SkuId, int RequestedQuantity);

public sealed class TransferOrder : IHasTimestamps
{
    private readonly List<TransferLine> _lines = [];

    private TransferOrder()
    {
        TransferNumber = string.Empty;
    }

    private TransferOrder(
        Guid requestId,
        string transferNumber,
        Guid sourceWarehouseId,
        Guid destinationWarehouseId,
        string? externalReference)
    {
        Id = Guid.NewGuid();
        RequestId = requestId;
        TransferNumber = transferNumber;
        SourceWarehouseId = sourceWarehouseId;
        DestinationWarehouseId = destinationWarehouseId;
        ExternalReference = externalReference;
        Status = TransferStatus.Created;
        var now = DateTime.UtcNow;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid RequestId { get; private set; }

    public string TransferNumber { get; private set; }

    public Guid SourceWarehouseId { get; private set; }

    public Guid DestinationWarehouseId { get; private set; }

    public string? ExternalReference { get; private set; }

    public TransferStatus Status { get; private set; }

    public Guid? OutboundOrderId { get; private set; }

    public Guid? InboundReceiptId { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime? ShippedAt { get; private set; }

    public DateTime? CompletedAt { get; private set; }

    public DateTime? CancelledAt { get; private set; }

    public DateTime UpdatedAt { get; set; }

    public IReadOnlyCollection<TransferLine> Lines => _lines;

    public int InTransitQuantity => _lines.Sum(l => l.InTransitQuantity);

    public static TransferOrder Create(
        Guid requestId,
        string transferNumber,
        Guid sourceWarehouseId,
        Guid destinationWarehouseId,
        string? externalReference,
        IReadOnlyList<TransferLineSpec> lineSpecs)
    {
        if (requestId == Guid.Empty)
        {
            throw new ArgumentException("Transfer bir RequestId taşımalıdır.", nameof(requestId));
        }

        if (string.IsNullOrWhiteSpace(transferNumber))
        {
            throw new ArgumentException("Transfer number boş olamaz.", nameof(transferNumber));
        }

        if (sourceWarehouseId == Guid.Empty || destinationWarehouseId == Guid.Empty)
        {
            throw new ArgumentException("Transfer için source ve destination warehouse zorunludur.");
        }

        if (sourceWarehouseId == destinationWarehouseId)
        {
            throw new ArgumentException("Source ve destination warehouse aynı olamaz — aynı depo içi hareket RelocateStock işidir.");
        }

        if (lineSpecs.Count == 0)
        {
            throw new ArgumentException("Transfer en az bir line içermelidir.", nameof(lineSpecs));
        }

        var transfer = new TransferOrder(
            requestId,
            transferNumber.Trim().ToUpperInvariant(),
            sourceWarehouseId,
            destinationWarehouseId,
            string.IsNullOrWhiteSpace(externalReference) ? null : externalReference.Trim());

        foreach (var spec in lineSpecs)
        {
            transfer._lines.Add(TransferLine.Create(transfer.Id, spec.SkuId, spec.RequestedQuantity));
        }

        return transfer;
    }

    public TransferLine GetLine(Guid lineId) =>
        _lines.FirstOrDefault(l => l.Id == lineId)
        ?? throw new InvalidOperationException($"Transfer line bulunamadı: {lineId}");

    public TransferLine GetLineBySku(Guid skuId) =>
        _lines.FirstOrDefault(l => l.SkuId == skuId)
        ?? throw new InvalidOperationException($"Transfer line bulunamadı: sku={skuId}");

    public void MarkAllocated(Guid outboundOrderId, DateTime? at = null)
    {
        if (Status == TransferStatus.Allocated)
        {
            return;
        }

        if (Status != TransferStatus.Created)
        {
            throw new InvalidOperationException($"Transfer {Status} durumundayken allocate edilemez.");
        }

        if (outboundOrderId == Guid.Empty)
        {
            throw new ArgumentException("Allocation bir outbound order'a bağlı olmalıdır.", nameof(outboundOrderId));
        }

        OutboundOrderId = outboundOrderId;
        Status = TransferStatus.Allocated;
    }

    public void MarkShipped(Guid inboundReceiptId, DateTime? at = null)
    {
        if (Status == TransferStatus.InTransit || Status == TransferStatus.Receiving)
        {
            return;
        }

        if (Status != TransferStatus.Allocated)
        {
            throw new InvalidOperationException($"Transfer {Status} durumundayken ship edilemez.");
        }

        if (inboundReceiptId == Guid.Empty)
        {
            throw new ArgumentException("Ship bir destination receipt'a bağlı olmalıdır.", nameof(inboundReceiptId));
        }

        InboundReceiptId = inboundReceiptId;
        Status = TransferStatus.InTransit;
        ShippedAt = at ?? DateTime.UtcNow;
    }

    public void MarkReceiving()
    {
        if (Status == TransferStatus.InTransit)
        {
            Status = TransferStatus.Receiving;
        }
    }

    public void MarkCompletedIfAllClosed(DateTime? at = null)
    {
        if (Status == TransferStatus.Completed)
        {
            return;
        }

        if (Status is not (TransferStatus.InTransit or TransferStatus.Receiving))
        {
            throw new InvalidOperationException($"Transfer {Status} durumundayken complete edilemez.");
        }

        if (!_lines.All(l => l.IsClosed))
        {
            throw new InvalidOperationException("Tüm line'lar kapanmadan transfer complete edilemez — dangling InTransit yasak.");
        }

        Status = TransferStatus.Completed;
        CompletedAt = at ?? DateTime.UtcNow;
    }

    public void Cancel(DateTime? at = null)
    {
        if (Status == TransferStatus.Cancelled)
        {
            return;
        }

        if (Status is not (TransferStatus.Created or TransferStatus.Allocated))
        {
            throw new InvalidOperationException(
                $"Shipment sonrası transfer iptal edilemez ({Status}) — ürün fiziksel olarak InTransit'tedir; reversal explicit workflow gerektirir.");
        }

        Status = TransferStatus.Cancelled;
        CancelledAt = at ?? DateTime.UtcNow;
    }
}
