namespace Wms.Modules.Inventory.Application;

public sealed class InventoryBalanceNotFoundException : InventoryNotFoundException
{
    public InventoryBalanceNotFoundException(Guid balanceId)
        : base($"Inventory balance bulunamadı: {balanceId}")
    {
    }
}
