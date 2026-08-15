namespace Wms.Modules.Fulfillment.Application.Optimization;

public sealed class OptimizationOptions
{
    public int MaxCandidateWarehouses { get; set; } = 8;

    public int MaxSplitWarehouses { get; set; } = 2;

    public int SolverTimeoutMs { get; set; } = 2000;

    public decimal BaseDispatchCost { get; set; } = 8.00m;

    public decimal CostPerKm { get; set; } = 0.35m;

    public decimal DriverCostPerMinute { get; set; } = 0.10m;

    public decimal TollCost { get; set; } = 0.00m;

    public decimal PackagingCostPerShipment { get; set; } = 4.00m;

    public decimal HandlingCostPerShipment { get; set; } = 2.00m;

    public decimal PickingCostPerUnit { get; set; } = 0.50m;

    public decimal SplitPenaltyCost { get; set; } = 6.00m;

    public decimal RiskPenaltyGreen { get; set; } = 0.00m;

    public decimal RiskPenaltyYellow { get; set; } = 1.50m;

    public decimal RiskPenaltyOrange { get; set; } = 3.50m;

    public decimal RiskPenaltyRed { get; set; } = 8.00m;

    public decimal ScarcityThresholdRatio { get; set; } = 0.2m;

    public decimal ScarcityPenaltyCost { get; set; } = 2.50m;

    public decimal SlaPenaltyCost { get; set; } = 0.00m;
}
