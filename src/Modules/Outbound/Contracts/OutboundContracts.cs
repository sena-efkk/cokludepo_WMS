namespace Wms.Modules.Outbound.Contracts;

public sealed record OutboundOrderLineInput(Guid SkuId, int RequestedQuantity);

public sealed record OutboundOrderCreated(Guid OrderId, string OrderNumber);

public enum OutboundAllocateOutcome
{
    Allocated = 1,
    AlreadyAllocated = 2,
    InsufficientStock = 3,
}

public sealed record OutboundAllocateResult(OutboundAllocateOutcome Outcome, Guid OrderId);

public enum OutboundShipOutcome
{
    Shipped = 1,
    AlreadyShipped = 2,
}

public sealed record OutboundShipResult(OutboundShipOutcome Outcome, Guid OrderId, Guid ShipmentId, string ShipmentNumber);

public sealed record OutboundOrderLineInfo(Guid Id, Guid SkuId, int RequestedQuantity, Guid? ReservationId);

public sealed record OutboundOrderInfo(
    Guid Id,
    string OrderNumber,
    Guid WarehouseId,
    string Status,
    IReadOnlyList<OutboundOrderLineInfo> Lines);

public interface IOutboundContract
{
    Task<OutboundOrderCreated> CreateOrderAsync(
        Guid requestId,
        string? orderNumber,
        Guid warehouseId,
        string? externalOrderReference,
        IReadOnlyList<OutboundOrderLineInput> lines,
        CancellationToken cancellationToken);

    Task<OutboundAllocateResult> AllocateOrderAsync(Guid orderId, CancellationToken cancellationToken);

    Task<OutboundOrderInfo?> GetOrderAsync(Guid orderId, CancellationToken cancellationToken);

    Task<OutboundShipResult> ShipOrderAsync(
        Guid requestId,
        Guid orderId,
        string? trackingNumber,
        string? carrierCode,
        CancellationToken cancellationToken);

    Task CancelOrderAsync(Guid orderId, CancellationToken cancellationToken);
}
