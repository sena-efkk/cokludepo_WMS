namespace Wms.Modules.Inbound.Domain;

public interface IHasTimestamps
{
    DateTime UpdatedAt { get; set; }
}
