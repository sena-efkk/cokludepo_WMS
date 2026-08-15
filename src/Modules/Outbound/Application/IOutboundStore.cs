using Wms.Modules.Outbound.Domain;

namespace Wms.Modules.Outbound.Application;

public enum OutboundSaveOutcome
{
    Saved = 1,
    DuplicateRequest = 2,
}

public interface IOutboundStore
{
    Task<FulfillmentOrder?> GetOrderAsync(Guid orderId, CancellationToken cancellationToken);

    Task<FulfillmentOrder?> GetOrderByRequestIdAsync(Guid requestId, CancellationToken cancellationToken);

    Task<FulfillmentOrder?> GetOrderByNumberAsync(string orderNumber, CancellationToken cancellationToken);

    Task<IReadOnlyList<FulfillmentOrder>> ListOrdersAsync(Guid? warehouseId, int limit, CancellationToken cancellationToken);

    Task AddOrderAsync(FulfillmentOrder order, CancellationToken cancellationToken);

    Task<PickTask?> GetPickTaskAsync(Guid taskId, CancellationToken cancellationToken);

    Task<IReadOnlyList<PickTask>> ListPickTasksAsync(Guid? warehouseId, PickTaskStatus? status, int limit, CancellationToken cancellationToken);

    Task<IReadOnlyList<PickTask>> ListPickTasksByOrderAsync(Guid orderId, CancellationToken cancellationToken);

    Task AddPickTaskAsync(PickTask task, CancellationToken cancellationToken);

    Task<Package?> GetPackageByOrderAsync(Guid orderId, CancellationToken cancellationToken);

    Task<Package?> GetPackageByRequestIdAsync(Guid requestId, CancellationToken cancellationToken);

    Task AddPackageAsync(Package package, CancellationToken cancellationToken);

    Task<Shipment?> GetShipmentByOrderAsync(Guid orderId, CancellationToken cancellationToken);

    Task<Shipment?> GetShipmentByRequestIdAsync(Guid requestId, CancellationToken cancellationToken);

    Task AddShipmentAsync(Shipment shipment, CancellationToken cancellationToken);

    Task AddOutboxMessageAsync(Wms.Integration.Outbox.OutboxMessage message, CancellationToken cancellationToken);

    Task BeginTransactionAsync(CancellationToken cancellationToken);

    Task CommitTransactionAsync(CancellationToken cancellationToken);

    Task RollbackTransactionAsync(CancellationToken cancellationToken);

    Task<OutboundSaveOutcome> SaveChangesAsync(CancellationToken cancellationToken);
}
