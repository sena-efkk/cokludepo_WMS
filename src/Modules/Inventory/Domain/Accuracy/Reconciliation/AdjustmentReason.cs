namespace Wms.Modules.Inventory.Domain.Accuracy.Reconciliation;

public enum AdjustmentReason
{
    CycleCountVariance = 1,
    Lost = 2,
    Found = 3,
    Damaged = 4,
    Misplaced = 5,
    DataCorrection = 6,
    Other = 7,
}
