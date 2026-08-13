namespace Wms.Modules.Inventory.Infrastructure.Persistence;

public sealed class InventoryOperation
{
    private InventoryOperation()
    {
        OperationType = string.Empty;
    }

    public InventoryOperation(Guid requestId, string operationType)
    {
        RequestId = requestId;
        OperationType = operationType;
        CreatedAt = DateTime.UtcNow;
    }

    public Guid RequestId { get; private set; }

    public string OperationType { get; private set; }

    public DateTime CreatedAt { get; private set; }
}
