using Wms.Modules.Outbound.Application;
using Wms.Modules.Outbound.Contracts;
using Wms.Integration.Telemetry;

namespace Wms.Modules.Outbound.Infrastructure;

public sealed class OutboundContractAdapter(
    CreateFulfillmentOrder createOrder,
    AllocateOrder allocateOrder,
    ShipOrder shipOrder,
    GetOrder getOrder,
    CancelOrder cancelOrder) : IOutboundContract
{
    public async Task<OutboundOrderCreated> CreateOrderAsync(
        Guid requestId,
        string? orderNumber,
        Guid warehouseId,
        string? externalOrderReference,
        IReadOnlyList<OutboundOrderLineInput> lines,
        CancellationToken cancellationToken)
    {
        var result = await createOrder.Handle(
            new CreateFulfillmentOrderCommand(
                requestId,
                orderNumber,
                warehouseId,
                externalOrderReference,
                lines.Select(l => new CreateFulfillmentOrderLineInput(l.SkuId, l.RequestedQuantity)).ToList()),
            cancellationToken);
        WmsMetrics.OrdersCreatedTotal.Add(1);
        return new OutboundOrderCreated(result.OrderId, result.OrderNumber);
    }

    public async Task<OutboundAllocateResult> AllocateOrderAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var result = await allocateOrder.Handle(orderId, cancellationToken);
        if (result.Outcome == AllocateOrderOutcome.InsufficientStock)
        {
            WmsMetrics.AllocationFailuresTotal.Add(1);
        }

        return new OutboundAllocateResult(
            result.Outcome switch
            {
                AllocateOrderOutcome.Allocated => OutboundAllocateOutcome.Allocated,
                AllocateOrderOutcome.InsufficientStock => OutboundAllocateOutcome.InsufficientStock,
                _ => OutboundAllocateOutcome.AlreadyAllocated,
            },
            result.OrderId);
    }

    public async Task<OutboundOrderInfo?> GetOrderAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var order = await getOrder.Handle(orderId, cancellationToken);
        return order is null
            ? null
            : new OutboundOrderInfo(
                order.Id,
                order.OrderNumber,
                order.WarehouseId,
                order.Status.ToString(),
                order.Lines
                    .Select(l => new OutboundOrderLineInfo(l.Id, l.SkuId, l.RequestedQuantity, l.ReservationId))
                    .ToList());
    }

    public async Task<OutboundShipResult> ShipOrderAsync(
        Guid requestId,
        Guid orderId,
        string? trackingNumber,
        string? carrierCode,
        CancellationToken cancellationToken)
    {
        var result = await shipOrder.Handle(
            new ShipOrderCommand(orderId, requestId, trackingNumber, carrierCode),
            cancellationToken);
        WmsMetrics.OrdersShippedTotal.Add(1);
        return new OutboundShipResult(
            result.Outcome == ShipOrderOutcome.Shipped ? OutboundShipOutcome.Shipped : OutboundShipOutcome.AlreadyShipped,
            result.OrderId,
            result.ShipmentId,
            result.ShipmentNumber);
    }

    public async Task CancelOrderAsync(Guid orderId, CancellationToken cancellationToken)
    {
        await cancelOrder.Handle(orderId, cancellationToken);
    }
}
