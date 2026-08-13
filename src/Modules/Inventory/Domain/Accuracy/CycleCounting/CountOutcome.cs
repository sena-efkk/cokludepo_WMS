namespace Wms.Modules.Inventory.Domain.Accuracy.CycleCounting;

public enum CountOutcome
{
    Verified = 1,
    VarianceDetected = 2,
    StaleRecountRequired = 3,
}
