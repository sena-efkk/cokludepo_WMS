namespace Wms.Modules.Inventory.Domain.Accuracy.Scanning;

public sealed class ScanMovementEvidence
{
    private ScanMovementEvidence()
    {
        SourceScanValue = string.Empty;
        SkuScanValue = string.Empty;
        DestinationScanValue = string.Empty;
        DeviceId = string.Empty;
        OperatorId = string.Empty;
    }

    private ScanMovementEvidence(
        Guid movementId,
        Guid requestId,
        Guid warehouseId,
        Guid skuId,
        Guid sourceLocationId,
        Guid destinationLocationId,
        string sourceScanValue,
        string skuScanValue,
        string destinationScanValue,
        int quantity,
        string? deviceId,
        string? operatorId,
        DateTime occurredAt)
    {
        Id = Guid.NewGuid();
        MovementId = movementId;
        RequestId = requestId;
        WarehouseId = warehouseId;
        SkuId = skuId;
        SourceLocationId = sourceLocationId;
        DestinationLocationId = destinationLocationId;
        SourceScanValue = sourceScanValue;
        SkuScanValue = skuScanValue;
        DestinationScanValue = destinationScanValue;
        Quantity = quantity;
        DeviceId = deviceId ?? string.Empty;
        OperatorId = operatorId ?? string.Empty;
        OccurredAt = occurredAt;
    }

    public Guid Id { get; private set; }

    public Guid MovementId { get; private set; }

    public Guid RequestId { get; private set; }

    public Guid WarehouseId { get; private set; }

    public Guid SkuId { get; private set; }

    public Guid SourceLocationId { get; private set; }

    public Guid DestinationLocationId { get; private set; }

    public string SourceScanValue { get; private set; }

    public string SkuScanValue { get; private set; }

    public string DestinationScanValue { get; private set; }

    public int Quantity { get; private set; }

    public string DeviceId { get; private set; }

    public string OperatorId { get; private set; }

    public DateTime OccurredAt { get; private set; }

    public static ScanMovementEvidence Create(
        Guid movementId,
        Guid requestId,
        Guid warehouseId,
        Guid skuId,
        Guid sourceLocationId,
        Guid destinationLocationId,
        string sourceScanValue,
        string skuScanValue,
        string destinationScanValue,
        int quantity,
        string? deviceId,
        string? operatorId,
        DateTime? occurredAt = null)
    {
        if (movementId == Guid.Empty)
        {
            throw new ArgumentException("Scan evidence bir movement'a bağlı olmalıdır.", nameof(movementId));
        }

        if (requestId == Guid.Empty)
        {
            throw new ArgumentException("Scan evidence request id boş olamaz.", nameof(requestId));
        }

        if (warehouseId == Guid.Empty || skuId == Guid.Empty || sourceLocationId == Guid.Empty || destinationLocationId == Guid.Empty)
        {
            throw new ArgumentException("Scan evidence; warehouse, SKU ve lokasyonlar zorunludur.");
        }

        if (string.IsNullOrWhiteSpace(sourceScanValue)
            || string.IsNullOrWhiteSpace(skuScanValue)
            || string.IsNullOrWhiteSpace(destinationScanValue))
        {
            throw new ArgumentException("Scan evidence; üç scan değeri de zorunludur (strict mode).");
        }

        if (quantity <= 0)
        {
            throw new ArgumentException("Scan evidence quantity pozitif olmalıdır.", nameof(quantity));
        }

        return new ScanMovementEvidence(
            movementId,
            requestId,
            warehouseId,
            skuId,
            sourceLocationId,
            destinationLocationId,
            sourceScanValue.Trim(),
            skuScanValue.Trim(),
            destinationScanValue.Trim(),
            quantity,
            string.IsNullOrWhiteSpace(deviceId) ? null : deviceId.Trim(),
            string.IsNullOrWhiteSpace(operatorId) ? null : operatorId.Trim(),
            occurredAt ?? DateTime.UtcNow);
    }
}
