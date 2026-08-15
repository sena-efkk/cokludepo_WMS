namespace Wms.Modules.Fulfillment.Application;

public sealed class SourcingOptions
{
    public int MaxSplitWarehouses { get; set; } = 2;

    public int MaxCandidateWarehouses { get; set; } = 8;

    public int BaseScore { get; set; } = 60;

    public int CompleteFulfillmentBonus { get; set; } = 25;

    public int SingleWarehouseBonus { get; set; } = 10;

    public int SplitPenaltyPoints { get; set; } = 20;

    public int RiskPenaltyGreen { get; set; } = 0;

    public int RiskPenaltyYellow { get; set; } = 8;

    public int RiskPenaltyOrange { get; set; } = 16;

    public int RiskPenaltyRed { get; set; } = 30;
}
