namespace Wms.Modules.Outbound.Domain;

public interface IHasTimestamps
{
    DateTime UpdatedAt { get; set; }
}
