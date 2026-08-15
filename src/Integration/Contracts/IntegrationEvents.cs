namespace Wms.Integration.Contracts;

public static class IntegrationEventTypes
{
    public const string ShipmentShipped = "outbound.shipment-shipped";
    public const string ReceiptCompleted = "inbound.receipt-completed";
    public const int CurrentVersion = 1;
}

public sealed record ShipmentShippedV1(
    Guid EventId,
    DateTime OccurredAt,
    Guid ShipmentId,
    Guid OrderId,
    string OrderNumber,
    Guid WarehouseId,
    Guid? CorrelationId = null);

public sealed record ReceiptCompletedV1(
    Guid EventId,
    DateTime OccurredAt,
    Guid ReceiptId,
    string ReceiptNumber,
    Guid WarehouseId,
    Guid? CorrelationId = null);
