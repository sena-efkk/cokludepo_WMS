using Wms.Modules.Outbound.Application;
using Wms.Modules.Outbound.Domain;

namespace Wms.Api.Outbound;

public sealed record CreateOrderLineRequest(Guid SkuId, int RequestedQuantity);

public sealed record CreateOrderRequest(
    Guid? RequestId,
    string? OrderNumber,
    Guid WarehouseId,
    string? ExternalOrderReference,
    IReadOnlyList<CreateOrderLineRequest> Lines);

public sealed record CreateOrderResponse(string Outcome, Guid OrderId, string OrderNumber);

public sealed record AllocateOrderResponse(string Outcome, Guid OrderId);

public sealed record OrderLineResponse(Guid Id, Guid SkuId, int RequestedQuantity, Guid? ReservationId);

public sealed record PickTaskResponse(
    Guid Id,
    Guid OrderId,
    Guid OrderLineId,
    Guid ReservationId,
    Guid ReservationLineId,
    Guid WarehouseId,
    Guid LocationId,
    Guid SkuId,
    int RequiredQuantity,
    int PickedQuantity,
    string Status,
    DateTime CreatedAt,
    DateTime? StartedAt,
    DateTime? CompletedAt)
{
    public static PickTaskResponse From(PickTaskQuery task) =>
        new(
            task.Id,
            task.OrderId,
            task.OrderLineId,
            task.ReservationId,
            task.ReservationLineId,
            task.WarehouseId,
            task.LocationId,
            task.SkuId,
            task.RequiredQuantity,
            task.PickedQuantity,
            task.Status.ToString(),
            task.CreatedAt,
            task.StartedAt,
            task.CompletedAt);
}

public sealed record PackageResponse(Guid Id, Guid OrderId, string PackageNumber, string Status, DateTime CreatedAt, DateTime PackedAt)
{
    public static PackageResponse From(Package package) =>
        new(package.Id, package.OrderId, package.PackageNumber, package.Status.ToString(), package.CreatedAt, package.PackedAt);
}

public sealed record ShipmentResponse(
    Guid Id,
    Guid OrderId,
    string ShipmentNumber,
    string Status,
    string? TrackingNumber,
    string? CarrierCode,
    DateTime CreatedAt,
    DateTime? ShippedAt)
{
    public static ShipmentResponse From(Shipment shipment) =>
        new(
            shipment.Id,
            shipment.OrderId,
            shipment.ShipmentNumber,
            shipment.Status.ToString(),
            shipment.TrackingNumber,
            shipment.CarrierCode,
            shipment.CreatedAt,
            shipment.ShippedAt);
}

public sealed record OrderResponse(
    Guid Id,
    Guid RequestId,
    string OrderNumber,
    Guid WarehouseId,
    string? ExternalOrderReference,
    string Status,
    DateTime CreatedAt,
    DateTime? AllocatedAt,
    DateTime? PickingStartedAt,
    DateTime? PackedAt,
    DateTime? ShippedAt,
    DateTime? CancelledAt,
    IReadOnlyList<OrderLineResponse> Lines,
    IReadOnlyList<PickTaskResponse> PickTasks,
    PackageResponse? Package,
    ShipmentResponse? Shipment)
{
    public static OrderResponse From(OrderQuery order) =>
        new(
            order.Id,
            order.RequestId,
            order.OrderNumber,
            order.WarehouseId,
            order.ExternalOrderReference,
            order.Status.ToString(),
            order.CreatedAt,
            order.AllocatedAt,
            order.PickingStartedAt,
            order.PackedAt,
            order.ShippedAt,
            order.CancelledAt,
            order.Lines
                .Select(l => new OrderLineResponse(l.Id, l.SkuId, l.RequestedQuantity, l.ReservationId))
                .ToList(),
            order.PickTasks.Select(PickTaskResponse.From).ToList(),
            order.Package is null ? null : PackageResponse.From(order.Package),
            order.Shipment is null ? null : ShipmentResponse.From(order.Shipment));
}

public sealed record OrderSummaryResponse(
    Guid Id,
    string OrderNumber,
    Guid WarehouseId,
    string? ExternalOrderReference,
    string Status,
    DateTime CreatedAt,
    int TotalRequested)
{
    public static OrderSummaryResponse From(OrderSummary summary) =>
        new(
            summary.Id,
            summary.OrderNumber,
            summary.WarehouseId,
            summary.ExternalOrderReference,
            summary.Status.ToString(),
            summary.CreatedAt,
            summary.TotalRequested);
}

public sealed record ConfirmPickRequest(string? LocationScan, string? SkuScan, int Quantity);

public sealed record ConfirmPickResponse(Guid TaskId, bool TaskCompleted, int PickedQuantity, int RemainingQuantity);

public sealed record NotFoundPickRequest(Guid? RequestId);

public sealed record PackOrderRequest(Guid? RequestId);

public sealed record PackOrderResponse(string Outcome, Guid OrderId, Guid PackageId, string PackageNumber);

public sealed record ShipOrderRequest(Guid? RequestId, string? TrackingNumber, string? CarrierCode);

public sealed record ShipOrderResponse(string Outcome, Guid OrderId, Guid ShipmentId, string ShipmentNumber);
