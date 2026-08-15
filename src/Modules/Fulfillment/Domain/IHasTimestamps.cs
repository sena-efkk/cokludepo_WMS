namespace Wms.Modules.Fulfillment.Domain;

public interface IHasTimestamps
{
    DateTime UpdatedAt { get; set; }
}
