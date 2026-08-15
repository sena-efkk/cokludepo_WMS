namespace Wms.Modules.Outbound.Domain;

public sealed class Package : IHasTimestamps
{
    private Package()
    {
        PackageNumber = string.Empty;
    }

    private Package(Guid orderId, Guid requestId, string packageNumber)
    {
        Id = Guid.NewGuid();
        OrderId = orderId;
        RequestId = requestId;
        PackageNumber = packageNumber;
        Status = PackageStatus.Packed;
        var now = DateTime.UtcNow;
        CreatedAt = now;
        PackedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid OrderId { get; private set; }

    public Guid RequestId { get; private set; }

    public string PackageNumber { get; private set; }

    public PackageStatus Status { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime PackedAt { get; private set; }

    public DateTime UpdatedAt { get; set; }

    public static Package Create(Guid orderId, Guid requestId, string packageNumber)
    {
        if (orderId == Guid.Empty)
        {
            throw new ArgumentException("Package bir order'a bağlı olmalıdır.", nameof(orderId));
        }

        if (requestId == Guid.Empty)
        {
            throw new ArgumentException("Package bir RequestId taşımalıdır.", nameof(requestId));
        }

        if (string.IsNullOrWhiteSpace(packageNumber))
        {
            throw new ArgumentException("Package number boş olamaz.", nameof(packageNumber));
        }

        return new Package(orderId, requestId, packageNumber.Trim().ToUpperInvariant());
    }
}
