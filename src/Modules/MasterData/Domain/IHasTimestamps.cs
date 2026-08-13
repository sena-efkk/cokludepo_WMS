namespace Wms.Modules.MasterData.Domain;

public interface IHasTimestamps
{
    DateTime UpdatedAt { get; set; }
}
