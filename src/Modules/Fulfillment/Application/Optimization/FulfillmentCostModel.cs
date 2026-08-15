using Wms.Modules.Fulfillment.Application;

namespace Wms.Modules.Fulfillment.Application.Optimization;

public sealed record CostBreakdown(
    decimal TransportCost,
    decimal DispatchCost,
    decimal PackagingCost,
    decimal HandlingCost,
    decimal PickingCost,
    decimal SplitPenalty,
    decimal InventoryReliabilityPenalty,
    decimal ScarcityPenalty,
    decimal SlaPenalty)
{
    public decimal TotalCost =>
        TransportCost
        + DispatchCost
        + PackagingCost
        + HandlingCost
        + PickingCost
        + SplitPenalty
        + InventoryReliabilityPenalty
        + ScarcityPenalty
        + SlaPenalty;
}

public sealed record CostInput(
    IReadOnlyList<(Guid WarehouseId, RouteInfo Route)> Routes,
    IReadOnlyList<SourcingWarehouseAssignment> Warehouses,
    IReadOnlyDictionary<(Guid WarehouseId, Guid SkuId), string> RiskByPair,
    IReadOnlyList<SourcingLineInput> Lines);

public sealed class FulfillmentCostModel(OptimizationOptions options)
{
    public CostBreakdown Evaluate(CostInput input)
    {
        var shipmentCount = input.Warehouses.Count;
        var totalDistanceKm = input.Routes.Sum(r => r.Route.DistanceKm);
        var totalDurationMinutes = input.Routes.Sum(r => r.Route.DurationMinutes);

        // Transport = mesafe + süre + toll (dispatch ayrı kalem — çift sayım yok).
        var transport = totalDistanceKm * options.CostPerKm
            + totalDurationMinutes * options.DriverCostPerMinute
            + shipmentCount * options.TollCost;

        var dispatch = shipmentCount * options.BaseDispatchCost;
        var packaging = shipmentCount * options.PackagingCostPerShipment;
        var handling = shipmentCount * options.HandlingCostPerShipment;
        var picking = input.Lines.Sum(l => l.Quantity) * options.PickingCostPerUnit;
        var split = (shipmentCount - 1) * options.SplitPenaltyCost;

        var reliability = 0m;
        foreach (var warehouse in input.Warehouses)
        {
            foreach (var line in warehouse.Lines)
            {
                var level = input.RiskByPair.GetValueOrDefault((warehouse.WarehouseId, line.SkuId));
                reliability += level switch
                {
                    "YELLOW" => options.RiskPenaltyYellow,
                    "ORANGE" => options.RiskPenaltyOrange,
                    "RED" => options.RiskPenaltyRed,
                    _ => options.RiskPenaltyGreen,
                };
            }
        }

        var scarcity = 0m;
        foreach (var warehouse in input.Warehouses)
        {
            foreach (var line in warehouse.Lines)
            {
                if (!line.Fulfillable || line.Atp <= 0)
                {
                    continue;
                }

                var remainingRatio = (decimal)(line.Atp - line.RequestedQuantity) / line.Atp;
                if (remainingRatio < options.ScarcityThresholdRatio)
                {
                    scarcity += options.ScarcityPenaltyCost;
                }
            }
        }

        var sla = options.SlaPenaltyCost;

        return new CostBreakdown(
            transport,
            dispatch,
            packaging,
            handling,
            picking,
            split,
            reliability,
            scarcity,
            sla);
    }
}
