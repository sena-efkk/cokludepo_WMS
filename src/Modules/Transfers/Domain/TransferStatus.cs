namespace Wms.Modules.Transfers.Domain;

public enum TransferStatus
{
    Created = 1,
    Allocated = 2,
    InTransit = 3,
    Receiving = 4,
    Completed = 5,
    Cancelled = 6,
    Exception = 7,
}
