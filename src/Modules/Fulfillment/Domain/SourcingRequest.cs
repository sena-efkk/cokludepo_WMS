namespace Wms.Modules.Fulfillment.Domain;

public sealed record SourcingLineSpec(Guid SkuId, int Quantity);

public sealed class SourcingRequest : IHasTimestamps
{
    private readonly List<SourcingLine> _lines = [];

    private SourcingRequest()
    {
        Destination = string.Empty;
    }

    private SourcingRequest(Guid requestId, string? destination)
    {
        Id = Guid.NewGuid();
        RequestId = requestId;
        Destination = destination ?? string.Empty;
        Status = SourcingStatus.Evaluated;
        var now = DateTime.UtcNow;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid RequestId { get; private set; }

    public string Destination { get; private set; }

    public SourcingStatus Status { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime UpdatedAt { get; set; }

    public IReadOnlyCollection<SourcingLine> Lines => _lines;

    public static SourcingRequest Create(Guid requestId, string? destination, IReadOnlyList<SourcingLineSpec> lineSpecs)
    {
        if (requestId == Guid.Empty)
        {
            throw new ArgumentException("Sourcing request bir RequestId taşımalıdır.", nameof(requestId));
        }

        if (lineSpecs.Count == 0)
        {
            throw new ArgumentException("Sourcing request en az bir line içermelidir.", nameof(lineSpecs));
        }

        var request = new SourcingRequest(
            requestId,
            string.IsNullOrWhiteSpace(destination) ? string.Empty : destination.Trim());

        foreach (var spec in lineSpecs)
        {
            request._lines.Add(SourcingLine.Create(request.Id, spec.SkuId, spec.Quantity));
        }

        return request;
    }

    public void MarkCommitted()
    {
        if (Status != SourcingStatus.Evaluated)
        {
            throw new InvalidOperationException($"Sourcing request {Status} durumundayken commit edilemez.");
        }

        Status = SourcingStatus.Committed;
    }

    public void MarkStale()
    {
        Status = SourcingStatus.Stale;
    }
}

public sealed class SourcingLine : IHasTimestamps
{
    private SourcingLine()
    {
    }

    private SourcingLine(Guid sourcingRequestId, Guid skuId, int quantity)
    {
        Id = Guid.NewGuid();
        SourcingRequestId = sourcingRequestId;
        SkuId = skuId;
        Quantity = quantity;
        var now = DateTime.UtcNow;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid SourcingRequestId { get; private set; }

    public Guid SkuId { get; private set; }

    public int Quantity { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime UpdatedAt { get; set; }

    public static SourcingLine Create(Guid sourcingRequestId, Guid skuId, int quantity)
    {
        if (sourcingRequestId == Guid.Empty)
        {
            throw new ArgumentException("Sourcing line bir request'e bağlı olmalıdır.", nameof(sourcingRequestId));
        }

        if (skuId == Guid.Empty)
        {
            throw new ArgumentException("Sourcing line bir SKU'ya bağlı olmalıdır.", nameof(skuId));
        }

        if (quantity <= 0)
        {
            throw new ArgumentException("Sourcing line quantity pozitif olmalıdır.", nameof(quantity));
        }

        return new SourcingLine(sourcingRequestId, skuId, quantity);
    }
}
