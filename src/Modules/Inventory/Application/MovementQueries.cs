using Wms.Modules.Inventory.Domain;

namespace Wms.Modules.Inventory.Application;

public sealed class GetMovement(IInventoryStore store)
{
    public async Task<InventoryMovement?> Handle(Guid movementId, CancellationToken cancellationToken)
    {
        return await store.GetMovementAsync(movementId, cancellationToken);
    }
}

public sealed class ListMovements(IInventoryStore store)
{
    public async Task<IReadOnlyList<InventoryMovement>> Handle(
        Guid? warehouseId,
        Guid? skuId,
        int limit,
        CancellationToken cancellationToken)
    {
        return await store.ListMovementsAsync(warehouseId, skuId, limit, cancellationToken);
    }
}
