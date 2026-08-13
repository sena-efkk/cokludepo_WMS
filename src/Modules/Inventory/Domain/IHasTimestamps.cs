namespace Wms.Modules.Inventory.Domain;

public interface IHasTimestamps
{
    DateTime UpdatedAt { get; set; }
}
