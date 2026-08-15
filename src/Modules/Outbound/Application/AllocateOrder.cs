using Wms.Modules.Inventory.Contracts;
using Wms.Modules.Outbound.Domain;

namespace Wms.Modules.Outbound.Application;

public enum AllocateOrderOutcome
{
    Allocated = 1,
    AlreadyAllocated = 2,
    InsufficientStock = 3,
}

public sealed record AllocateOrderResult(AllocateOrderOutcome Outcome, Guid OrderId);

public sealed class AllocateOrder(
    IOutboundStore store,
    IInventoryContract inventory)
{
    public async Task<AllocateOrderResult> Handle(Guid orderId, CancellationToken cancellationToken)
    {
        var order = await store.GetOrderAsync(orderId, cancellationToken)
            ?? throw new OrderNotFoundException(orderId);

        if (order.Status == OrderStatus.Allocated)
        {
            return new AllocateOrderResult(AllocateOrderOutcome.AlreadyAllocated, order.Id);
        }

        if (order.Status is not (OrderStatus.Created or OrderStatus.AllocationFailed))
        {
            throw new InvalidOrderStateException($"Order {order.Status} durumundayken allocate edilemez.");
        }

        var lineInputs = order.Lines
            .Select(l => new ReserveOrderLineInput(l.SkuId, l.RequestedQuantity))
            .ToList();

        // Order.RequestId = stable allocation correlation → retry idempotent.
        var reserveResult = await inventory.ReserveOrderAsync(
            order.RequestId,
            order.WarehouseId,
            lineInputs,
            "OUTBOUND_ORDER",
            cancellationToken);

        if (reserveResult.Outcome == ReserveOrderOutcome.InsufficientStock)
        {
            await store.BeginTransactionAsync(cancellationToken);
            try
            {
                var fresh = await store.GetOrderAsync(orderId, cancellationToken)
                    ?? throw new OrderNotFoundException(orderId);
                fresh.MarkAllocationFailed();
                await store.SaveChangesAsync(cancellationToken);
                await store.CommitTransactionAsync(cancellationToken);
            }
            catch
            {
                await store.RollbackTransactionAsync(cancellationToken);
                throw;
            }

            return new AllocateOrderResult(AllocateOrderOutcome.InsufficientStock, order.Id);
        }

        // Reserved veya AlreadyRecorded (crash recovery) — aynı güvenli tamamlama yolu.
        await store.BeginTransactionAsync(cancellationToken);
        try
        {
            var fresh = await store.GetOrderAsync(orderId, cancellationToken)
                ?? throw new OrderNotFoundException(orderId);

            if (fresh.Status == OrderStatus.Allocated)
            {
                await store.CommitTransactionAsync(cancellationToken);
                return new AllocateOrderResult(AllocateOrderOutcome.AlreadyAllocated, order.Id);
            }

            if (fresh.Status is not (OrderStatus.Created or OrderStatus.AllocationFailed))
            {
                throw new InvalidOrderStateException($"Order {fresh.Status} durumundayken allocate edilemez.");
            }

            var existingTasks = await store.ListPickTasksByOrderAsync(orderId, cancellationToken);
            if (existingTasks.Count > 0)
            {
                await store.CommitTransactionAsync(cancellationToken);
                return new AllocateOrderResult(AllocateOrderOutcome.AlreadyAllocated, order.Id);
            }

            foreach (var reservation in reserveResult.Reservations)
            {
                var line = fresh.GetLineBySku(reservation.SkuId);
                if (line.ReservationId is null)
                {
                    line.SetReservation(reservation.ReservationId);
                }
            }

            foreach (var reservation in reserveResult.Reservations)
            {
                var line = fresh.GetLineBySku(reservation.SkuId);
                foreach (var reservationLine in reservation.Lines)
                {
                    var task = PickTask.Create(
                        fresh.Id,
                        line.Id,
                        reservation.ReservationId,
                        reservationLine.ReservationLineId,
                        fresh.WarehouseId,
                        reservationLine.LocationId,
                        reservation.SkuId,
                        reservationLine.Quantity);
                    await store.AddPickTaskAsync(task, cancellationToken);
                }
            }

            fresh.MarkAllocated();

            var outcome = await store.SaveChangesAsync(cancellationToken);
            await store.CommitTransactionAsync(cancellationToken);

            if (outcome == OutboundSaveOutcome.DuplicateRequest)
            {
                return new AllocateOrderResult(AllocateOrderOutcome.AlreadyAllocated, order.Id);
            }

            return new AllocateOrderResult(AllocateOrderOutcome.Allocated, order.Id);
        }
        catch
        {
            await store.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }
}
