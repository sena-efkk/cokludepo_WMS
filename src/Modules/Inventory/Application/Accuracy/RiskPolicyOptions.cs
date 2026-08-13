namespace Wms.Modules.Inventory.Application.Accuracy;

public sealed class RiskPolicyOptions
{
    public int SlowAfterDays { get; set; } = 30;

    public int DeadAfterDays { get; set; } = 180;

    public int LongInactivity90Points { get; set; } = 15;

    public int LongInactivity180Points { get; set; } = 30;

    public int LongInactivity360Points { get; set; } = 45;

    public int RecentNotFoundPoints { get; set; } = 20;

    public int RepeatedNotFoundThreshold { get; set; } = 2;

    public int RepeatedNotFoundPoints { get; set; } = 45;

    public int PickingLocationPoints { get; set; } = 10;

    public int LowVelocityPoints { get; set; } = 5;

    public int DeadStockPoints { get; set; } = 10;

    public int GreenThreshold { get; set; } = 30;

    public int YellowThreshold { get; set; } = 60;

    public int OrangeThreshold { get; set; } = 80;

    public double AbcARatio { get; set; } = 0.2;

    public double AbcBRatio { get; set; } = 0.5;

    public int LargeVarianceThreshold { get; set; } = 50;
}
