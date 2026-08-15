using Wms.Modules.Inventory.Contracts;
using Wms.Modules.Outbound.Domain;

namespace Wms.Modules.Outbound.Application;

public sealed class CancelOrder(
    IOutboundStore store,
    IInventoryContract inventory)
{
    public async Task<FulfillmentOrder> Handle(Guid orderId, CancellationToken cancellationToken)
    {
        var order = await store.GetOrderAsync(orderId, cancellationToken)
            ?? throw new OrderNotFoundException(orderId);

        if (order.Status == OrderStatus.Cancelled)
        {
            return order;
        }

        if (order.Status == OrderStatus.Shipped)
        {
            throw new InvalidOrderStateException(
                "Shipped order normal cancel edilemez — bu Return / reverse logistics alanıdır.");
        }

        // Reservation release başarılı olmadan cancellation completed sayılmaz.
        foreach (var line in order.Lines)
        {
            if (line.ReservationId is null)
            {
                continue;
            }

            var detail = await inventory.GetReservationAsync(line.ReservationId.Value, cancellationToken);
            if (detail is null || detail.Status == "ALLOCATED")
            {
                await inventory.ReleaseReservationAsync(line.ReservationId.Value, cancellationToken);
            }
            else if (detail.Status == "CONSUMED")
            {
                throw new InvalidOrderStateException(
                    $"Rezervasyon consume edilmiş order iptal edilemez (line: {line.Id}).");
            }
        }

        await store.BeginTransactionAsync(cancellationToken);
        try
        {
            var fresh = await store.GetOrderAsync(orderId, cancellationToken)
                ?? throw new OrderNotFoundException(orderId);

            fresh.Cancel();

            var tasks = await store.ListPickTasksByOrderAsync(orderId, cancellationToken);
            foreach (var task in tasks)
            {
                task.Cancel();
            }

            await store.SaveChangesAsync(cancellationToken);
            await store.CommitTransactionAsync(cancellationToken);

            return fresh;
        }
        catch
        {
            await store.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }
}
