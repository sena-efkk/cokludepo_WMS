namespace Wms.Modules.Transfers.Domain;

public enum TransferDiscrepancyReason
{
    Short = 1,
    DamagedInTransit = 2,
    Lost = 3,
    Over = 4,
    Other = 5,
}
