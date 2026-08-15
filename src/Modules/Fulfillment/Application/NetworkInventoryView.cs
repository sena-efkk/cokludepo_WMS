using Wms.Modules.Facility.Contracts;
using Wms.Modules.Inventory.Contracts;
using Wms.Modules.MasterData.Contracts;
using Wms.Modules.Transfers.Contracts;

namespace Wms.Modules.Fulfillment.Application;

public sealed record NetworkSkuWarehouse(
    Guid WarehouseId,
    string WarehouseCode,
    bool IsOperational,
    int PhysicalStock,
    int Allocated,
    int Atp,
    int Hold,
    int Quarantine,
    int Damaged,
    string? RiskLevel,
    int? RiskScore,
    int? RecentNotFoundCount);

public sealed record SkuNetworkView(
    Guid SkuId,
    string SkuCode,
    int NetworkPhysicalStock,
    int NetworkAtp,
    int NetworkAllocated,
    IReadOnlyList<NetworkSkuWarehouse> Warehouses);

public sealed record NetworkSkuRow(
    Guid SkuId,
    string SkuCode,
    Guid WarehouseId,
    string WarehouseCode,
    bool IsOperational,
    int PhysicalStock,
    int Allocated,
    int Atp,
    int Hold,
    int Quarantine,
    int Damaged,
    string? RiskLevel,
    int? RiskScore);

public sealed record NetworkSkuPage(
    IReadOnlyList<NetworkSkuRow> Rows,
    int Total,
    int Page,
    int PageSize);

public sealed record ListNetworkSkusFilter(
    Guid? WarehouseId,
    bool? HasStock,
    bool? HasAtp,
    string? RiskLevel,
    string? Search,
    string? Sort,
    int Page,
    int PageSize);

public sealed record WarehouseNetworkSkuRow(
    Guid SkuId,
    string SkuCode,
    int PhysicalStock,
    int Allocated,
    int Atp,
    int Hold,
    int Quarantine,
    int Damaged,
    string? RiskLevel,
    int? RiskScore);

public sealed record WarehouseNetworkView(
    Guid WarehouseId,
    string WarehouseCode,
    bool IsOperational,
    int SkuCount,
    int PhysicalStock,
    int Allocated,
    int Atp,
    int Hold,
    int Quarantine,
    int Damaged,
    IReadOnlyList<WarehouseNetworkSkuRow> Skus,
    int Page,
    int PageSize);

public sealed record NetworkWarehouseSummaryRow(
    Guid WarehouseId,
    string Code,
    bool IsOperational,
    int PhysicalStock,
    int Atp,
    int Allocated,
    int Hold,
    int Quarantine,
    int Damaged,
    int SkuCount);

public sealed record NetworkSummary(
    int TotalWarehouses,
    int ActiveWarehouses,
    int PhysicalStock,
    int Atp,
    int Allocated,
    int Hold,
    int Quarantine,
    int Damaged,
    IReadOnlyList<NetworkWarehouseSummaryRow> Warehouses);

public sealed record OrderAvailabilityLineInput(Guid SkuId, int Quantity);

public sealed record OrderAvailabilityWarehouse(
    Guid WarehouseId,
    string Code,
    bool IsOperational,
    int Atp,
    bool CanSatisfy,
    string? RiskLevel);

public sealed record OrderAvailabilityLine(
    Guid SkuId,
    string SkuCode,
    int RequestedQuantity,
    int NetworkAtp,
    bool IsSatisfiable,
    IReadOnlyList<OrderAvailabilityWarehouse> Warehouses);

public sealed class NetworkInventoryView(
    IMasterDataQueryContract masterData,
    IInventoryContract inventory,
    IFacilityQueryContract facility,
    ITransferContract transfers)
{
    public async Task<SkuNetworkView?> GetSkuAsync(Guid skuId, CancellationToken cancellationToken)
    {
        var sku = await masterData.GetSkuAsync(skuId, cancellationToken);
        if (sku is null)
        {
            return null;
        }

        var availabilities = await inventory.ListSkuWarehouseAvailabilityAsync(skuId, cancellationToken);
        var riskPairs = availabilities
            .Select(a => new NetworkRiskPair(a.WarehouseId, skuId))
            .ToList();
        var risks = await inventory.ListSkuWarehouseRiskBatchAsync(riskPairs, cancellationToken);
        var riskByWarehouse = risks.ToDictionary(r => r.WarehouseId);

        var warehouses = await BuildWarehouseRowsAsync(availabilities, riskByWarehouse, cancellationToken);
        var inTransit = await transfers.GetOpenInTransitBySkuAsync(skuId, cancellationToken);

        return new SkuNetworkView(
            skuId,
            sku.Code,
            availabilities.Sum(a => a.PhysicalStock) + inTransit,
            availabilities.Sum(a => a.Atp),
            availabilities.Sum(a => a.Allocated),
            warehouses);
    }

    public async Task<NetworkSkuPage> ListSkusAsync(ListNetworkSkusFilter filter, CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid>? skuIds = null;
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            skuIds = await masterData.SearchSkuIdsAsync(filter.Search.Trim(), 500, cancellationToken);
            if (skuIds.Count == 0)
            {
                return new NetworkSkuPage([], 0, filter.Page, filter.PageSize);
            }
        }

        var needsRisk = !string.IsNullOrWhiteSpace(filter.RiskLevel)
                        || string.Equals(filter.Sort, "risk", StringComparison.OrdinalIgnoreCase);

        var skip = Math.Max(0, (filter.Page - 1) * filter.PageSize);

        if (!needsRisk)
        {
            var (rows, total) = await inventory.ListSkuWarehousePageAsync(
                filter.WarehouseId,
                skuIds,
                filter.HasStock,
                filter.HasAtp,
                NormalizeSort(filter.Sort),
                skip,
                filter.PageSize,
                cancellationToken);

            var skuCodes = await GetSkuCodesAsync(rows.Select(r => r.SkuId).Distinct().ToList(), cancellationToken);
            var facilityRows = await BuildNetworkRowsAsync(rows, skuCodes, null, cancellationToken);

            return new NetworkSkuPage(facilityRows, total, filter.Page, filter.PageSize);
        }

        var (candidateRows, _) = await inventory.ListSkuWarehousePageAsync(
            filter.WarehouseId,
            skuIds,
            filter.HasStock,
            filter.HasAtp,
            null,
            0,
            500,
            cancellationToken);

        var pairs = candidateRows
            .Select(r => new NetworkRiskPair(r.WarehouseId, r.SkuId))
            .ToList();
        var risks = await inventory.ListSkuWarehouseRiskBatchAsync(pairs, cancellationToken);
        var riskByPair = risks.ToDictionary(r => (r.WarehouseId, r.SkuId));

        var riskSkuCodes = await GetSkuCodesAsync(candidateRows.Select(r => r.SkuId).Distinct().ToList(), cancellationToken);
        var withRisk = (await BuildNetworkRowsAsync(candidateRows, riskSkuCodes, riskByPair, cancellationToken)).ToList();

        if (!string.IsNullOrWhiteSpace(filter.RiskLevel))
        {
            var level = filter.RiskLevel!.Trim().ToUpperInvariant();
            withRisk = withRisk.Where(r => string.Equals(r.RiskLevel, level, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        var sorted = SortRows(withRisk, filter.Sort);
        var paged = sorted.Skip(skip).Take(filter.PageSize).ToList();

        return new NetworkSkuPage(paged, sorted.Count, filter.Page, filter.PageSize);
    }

    public async Task<WarehouseNetworkView?> GetWarehouseAsync(Guid warehouseId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var warehouse = await facility.GetWarehouseAsync(warehouseId, cancellationToken);
        if (warehouse is null)
        {
            return null;
        }

        var skip = Math.Max(0, (page - 1) * pageSize);
        var (rows, total) = await inventory.ListWarehouseSkuRowsAsync(warehouseId, skip, pageSize, cancellationToken);

        var skuCodes = await GetSkuCodesAsync(rows.Select(r => r.SkuId).Distinct().ToList(), cancellationToken);
        var pairs = rows.Select(r => new NetworkRiskPair(warehouseId, r.SkuId)).ToList();
        var risks = await inventory.ListSkuWarehouseRiskBatchAsync(pairs, cancellationToken);
        var riskBySku = risks.ToDictionary(r => r.SkuId);

        var rollups = await inventory.ListWarehouseStockRollupsAsync(cancellationToken);
        var rollup = rollups.FirstOrDefault(r => r.WarehouseId == warehouseId);

        var skuRows = rows
            .Select(r => new WarehouseNetworkSkuRow(
                r.SkuId,
                skuCodes.GetValueOrDefault(r.SkuId, "?"),
                r.PhysicalStock,
                r.Allocated,
                r.Atp,
                r.Hold,
                r.Quarantine,
                r.Damaged,
                riskBySku.GetValueOrDefault(r.SkuId)?.RiskLevel,
                riskBySku.GetValueOrDefault(r.SkuId)?.RiskScore))
            .ToList();

        return new WarehouseNetworkView(
            warehouseId,
            warehouse.Code,
            warehouse.IsActive,
            total,
            rollup?.PhysicalStock ?? 0,
            rollup?.Allocated ?? 0,
            rollup?.Atp ?? 0,
            rollup?.Hold ?? 0,
            rollup?.Quarantine ?? 0,
            rollup?.Damaged ?? 0,
            skuRows,
            page,
            pageSize);
    }

    public async Task<NetworkSummary> GetSummaryAsync(CancellationToken cancellationToken)
    {
        var rollups = await inventory.ListWarehouseStockRollupsAsync(cancellationToken);
        var activeWarehouses = await facility.GetActiveWarehousesAsync(cancellationToken);
        var allWarehouses = new List<WarehouseInfo>();

        foreach (var rollup in rollups)
        {
            var info = activeWarehouses.FirstOrDefault(w => w.Id == rollup.WarehouseId)
                       ?? await facility.GetWarehouseAsync(rollup.WarehouseId, cancellationToken);
            if (info is not null)
            {
                allWarehouses.Add(info);
            }
        }

        var warehouses = rollups
            .Select(r => new NetworkWarehouseSummaryRow(
                r.WarehouseId,
                allWarehouses.FirstOrDefault(w => w.Id == r.WarehouseId)?.Code ?? "?",
                allWarehouses.FirstOrDefault(w => w.Id == r.WarehouseId)?.IsActive ?? false,
                r.PhysicalStock,
                r.Atp,
                r.Allocated,
                r.Hold,
                r.Quarantine,
                r.Damaged,
                r.SkuCount))
            .ToList();

        var inTransit = await transfers.GetOpenInTransitTotalAsync(cancellationToken);

        return new NetworkSummary(
            allWarehouses.Count,
            allWarehouses.Count(w => w.IsActive),
            rollups.Sum(r => r.PhysicalStock) + inTransit,
            rollups.Sum(r => r.Atp),
            rollups.Sum(r => r.Allocated),
            rollups.Sum(r => r.Hold),
            rollups.Sum(r => r.Quarantine),
            rollups.Sum(r => r.Damaged),
            warehouses);
    }

    public async Task<IReadOnlyList<OrderAvailabilityLine>> GetOrderAvailabilityAsync(
        IReadOnlyList<OrderAvailabilityLineInput> lines,
        CancellationToken cancellationToken)
    {
        var skuCodes = await GetSkuCodesAsync(lines.Select(l => l.SkuId).Distinct().ToList(), cancellationToken);
        var results = new List<OrderAvailabilityLine>();

        foreach (var line in lines)
        {
            var availabilities = await inventory.ListSkuWarehouseAvailabilityAsync(line.SkuId, cancellationToken);
            var pairs = availabilities.Select(a => new NetworkRiskPair(a.WarehouseId, line.SkuId)).ToList();
            var risks = await inventory.ListSkuWarehouseRiskBatchAsync(pairs, cancellationToken);
            var riskByWarehouse = risks.ToDictionary(r => r.WarehouseId);

            var warehouses = await BuildOrderAvailabilityWarehousesAsync(availabilities, riskByWarehouse, line.Quantity, cancellationToken);

            results.Add(new OrderAvailabilityLine(
                line.SkuId,
                skuCodes.GetValueOrDefault(line.SkuId, "?"),
                line.Quantity,
                availabilities.Sum(a => a.Atp),
                availabilities.Sum(a => a.Atp) >= line.Quantity,
                warehouses));
        }

        return results;
    }

    private async Task<IReadOnlyList<NetworkSkuWarehouse>> BuildWarehouseRowsAsync(
        IReadOnlyList<SkuWarehouseAvailability> availabilities,
        IReadOnlyDictionary<Guid, SkuWarehouseRisk> riskByWarehouse,
        CancellationToken cancellationToken)
    {
        var active = await facility.GetActiveWarehousesAsync(cancellationToken);
        var result = new List<NetworkSkuWarehouse>();

        foreach (var availability in availabilities)
        {
            var info = active.FirstOrDefault(w => w.Id == availability.WarehouseId)
                       ?? await facility.GetWarehouseAsync(availability.WarehouseId, cancellationToken);
            var risk = riskByWarehouse.GetValueOrDefault(availability.WarehouseId);
            result.Add(new NetworkSkuWarehouse(
                availability.WarehouseId,
                info?.Code ?? "?",
                info?.IsActive ?? false,
                availability.PhysicalStock,
                availability.Allocated,
                availability.Atp,
                availability.Hold,
                availability.Quarantine,
                availability.Damaged,
                risk?.RiskLevel,
                risk?.RiskScore,
                risk?.RecentNotFoundCount));
        }

        return result;
    }

    private async Task<IReadOnlyList<NetworkSkuRow>> BuildNetworkRowsAsync(
        IReadOnlyList<SkuWarehouseAvailability> rows,
        IReadOnlyDictionary<Guid, string> skuCodes,
        IReadOnlyDictionary<(Guid WarehouseId, Guid SkuId), SkuWarehouseRisk>? riskByPair,
        CancellationToken cancellationToken)
    {
        var active = await facility.GetActiveWarehousesAsync(cancellationToken);
        var result = new List<NetworkSkuRow>();

        foreach (var row in rows)
        {
            var info = active.FirstOrDefault(w => w.Id == row.WarehouseId)
                       ?? await facility.GetWarehouseAsync(row.WarehouseId, cancellationToken);
            var risk = riskByPair?.GetValueOrDefault((row.WarehouseId, row.SkuId));
            result.Add(new NetworkSkuRow(
                row.SkuId,
                skuCodes.GetValueOrDefault(row.SkuId, "?"),
                row.WarehouseId,
                info?.Code ?? "?",
                info?.IsActive ?? false,
                row.PhysicalStock,
                row.Allocated,
                row.Atp,
                row.Hold,
                row.Quarantine,
                row.Damaged,
                risk?.RiskLevel,
                risk?.RiskScore));
        }

        return result;
    }

    private async Task<IReadOnlyList<OrderAvailabilityWarehouse>> BuildOrderAvailabilityWarehousesAsync(
        IReadOnlyList<SkuWarehouseAvailability> availabilities,
        IReadOnlyDictionary<Guid, SkuWarehouseRisk> riskByWarehouse,
        int requestedQuantity,
        CancellationToken cancellationToken)
    {
        var active = await facility.GetActiveWarehousesAsync(cancellationToken);
        var result = new List<OrderAvailabilityWarehouse>();

        foreach (var availability in availabilities)
        {
            var info = active.FirstOrDefault(w => w.Id == availability.WarehouseId)
                       ?? await facility.GetWarehouseAsync(availability.WarehouseId, cancellationToken);
            var risk = riskByWarehouse.GetValueOrDefault(availability.WarehouseId);
            result.Add(new OrderAvailabilityWarehouse(
                availability.WarehouseId,
                info?.Code ?? "?",
                info?.IsActive ?? false,
                availability.Atp,
                availability.Atp >= requestedQuantity,
                risk?.RiskLevel));
        }

        return result;
    }

    private async Task<IReadOnlyDictionary<Guid, string>> GetSkuCodesAsync(
        IReadOnlyList<Guid> skuIds,
        CancellationToken cancellationToken)
    {
        if (skuIds.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        var skus = await masterData.GetSkusByIdsAsync(skuIds, cancellationToken);
        return skus.ToDictionary(s => s.Id, s => s.Code);
    }

    private static string? NormalizeSort(string? sort) =>
        sort?.ToLowerInvariant() switch
        {
            "atp" => "atp",
            "physical" => "physical",
            _ => null,
        };

    private static List<NetworkSkuRow> SortRows(List<NetworkSkuRow> rows, string? sort)
    {
        return sort?.ToLowerInvariant() switch
        {
            "risk" => [.. rows.OrderByDescending(r => r.RiskScore ?? 0)],
            "atp" => [.. rows.OrderByDescending(r => r.Atp)],
            "physical" => [.. rows.OrderByDescending(r => r.PhysicalStock)],
            _ => [.. rows.OrderBy(r => r.SkuId).ThenBy(r => r.WarehouseId)],
        };
    }
}
