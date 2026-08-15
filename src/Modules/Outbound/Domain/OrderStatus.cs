namespace Wms.Modules.Outbound.Domain;

public enum OrderStatus
{
    Created = 1,
    Allocated = 2,
    Picking = 3,
    Picked = 4,
    Packed = 5,
    Shipped = 6,
    Cancelled = 7,
    AllocationFailed = 8,
    PickException = 9,
}
