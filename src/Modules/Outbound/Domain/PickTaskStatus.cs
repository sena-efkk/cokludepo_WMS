namespace Wms.Modules.Outbound.Domain;

public enum PickTaskStatus
{
    Pending = 1,
    InProgress = 2,
    Completed = 3,
    NotFound = 4,
    Cancelled = 5,
}
