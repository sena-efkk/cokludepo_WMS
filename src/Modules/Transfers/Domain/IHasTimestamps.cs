namespace Wms.Modules.Transfers.Domain;

public interface IHasTimestamps
{
    DateTime UpdatedAt { get; set; }
}
