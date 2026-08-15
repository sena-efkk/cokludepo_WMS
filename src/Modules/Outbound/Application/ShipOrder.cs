using Wms.Modules.Inventory.Contracts;
using Wms.Modules.Outbound.Domain;
using Wms.Integration.Contracts;
using Wms.Integration.Outbox;

namespace Wms.Modules.Outbound.Application;

public enum ShipOrderOutcome
{
    Shipped = 1,
    AlreadyShipped = 2,
}

public sealed record ShipOrderCommand(
    Guid OrderId,
    Guid RequestId,
    string? TrackingNumber = null,
    string? CarrierCode = null);

public sealed record ShipOrderResult(
    ShipOrderOutcome Outcome,
    Guid OrderId,
    Guid ShipmentId,
    string ShipmentNumber);

public sealed class ShipOrder(
    IOutboundStore store,
    IInventoryContract inventory)
{
    public async Task<ShipOrderResult> Handle(ShipOrderCommand command, CancellationToken cancellationToken)
    {
        var order = await store.GetOrderAsync(command.OrderId, cancellationToken)
            ?? throw new OrderNotFoundException(command.OrderId);

        if (order.Status == OrderStatus.Shipped)
        {
            var existingShipment = await store.GetShipmentByOrderAsync(order.Id, cancellationToken);
            if (existingShipment is not null)
            {
                return new ShipOrderResult(ShipOrderOutcome.AlreadyShipped, order.Id, existingShipment.Id, existingShipment.ShipmentNumber);
            }
        }

        if (order.Status != OrderStatus.Packed)
        {
            throw new InvalidOrderStateException(
                $"Order yalnızca PACKED durumundayken ship edilebilir. Mevcut: {order.Status}");
        }

        var existing = await store.GetShipmentByRequestIdAsync(command.RequestId, cancellationToken);
        if (existing is not null)
        {
            return new ShipOrderResult(
                existing.Status == ShipmentStatus.Shipped ? ShipOrderOutcome.AlreadyShipped : ShipOrderOutcome.Shipped,
                order.Id,
                existing.Id,
                existing.ShipmentNumber);
        }

        // 1) Inventory consume (idempotent — CONSUMED rezervasyonda ikinci çağrı no-op).
        foreach (var line in order.Lines)
        {
            if (line.ReservationId is null)
            {
                throw new InvalidOrderStateException(
                    $"Order line rezervasyonsuz ship edilemez: {line.Id} — allocation bozuk.");
            }

            await inventory.ConsumeReservationAsync(line.ReservationId.Value, cancellationToken);
        }

        // 2) Outbound state (tek transaction; crash sonrası retry yukarıda idempotent).
        var shipment = Shipment.Create(
            order.Id,
            command.RequestId,
            $"SHP-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}",
            command.TrackingNumber,
            command.CarrierCode);

        await store.BeginTransactionAsync(cancellationToken);
        try
        {
            var fresh = await store.GetOrderAsync(command.OrderId, cancellationToken)
                ?? throw new OrderNotFoundException(command.OrderId);

            if (fresh.Status != OrderStatus.Packed)
            {
                throw new InvalidOrderStateException(
                    $"Order yalnızca PACKED durumundayken ship edilebilir. Mevcut: {fresh.Status}");
            }

            shipment.MarkShipped();
            fresh.MarkShipped(shipment.ShippedAt);

            await store.AddShipmentAsync(shipment, cancellationToken);

            // Integration event: business state + outbox AYNI transaction'da (atomic).
            var shippedEvent = new ShipmentShippedV1(
                shipment.Id,
                shipment.ShippedAt!.Value,
                shipment.Id,
                fresh.Id,
                fresh.OrderNumber,
                fresh.WarehouseId,
                fresh.Id);
            var outbox = OutboxMessage.Create(
                shipment.Id,
                IntegrationEventTypes.ShipmentShipped,
                IntegrationEventTypes.CurrentVersion,
                shippedEvent,
                shipment.ShippedAt.Value,
                correlationId: fresh.Id);
            await store.AddOutboxMessageAsync(outbox, cancellationToken);

            var outcome = await store.SaveChangesAsync(cancellationToken);
            await store.CommitTransactionAsync(cancellationToken);

            if (outcome == OutboundSaveOutcome.DuplicateRequest)
            {
                var winner = await store.GetShipmentByRequestIdAsync(command.RequestId, cancellationToken);
                if (winner is not null)
                {
                    return new ShipOrderResult(ShipOrderOutcome.AlreadyShipped, order.Id, winner.Id, winner.ShipmentNumber);
                }

                throw new InvalidOperationException($"Ship çakıştı ama shipment bulunamadı: {command.RequestId}");
            }

            return new ShipOrderResult(ShipOrderOutcome.Shipped, order.Id, shipment.Id, shipment.ShipmentNumber);
        }
        catch
        {
            await store.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }
}
