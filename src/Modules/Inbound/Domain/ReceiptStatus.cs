namespace Wms.Modules.Inbound.Domain;

public enum ReceiptStatus
{
    Open = 1,
    PartiallyReceived = 2,
    Received = 3,
    PutawayInProgress = 4,
    Completed = 5,
    Cancelled = 6,
}
