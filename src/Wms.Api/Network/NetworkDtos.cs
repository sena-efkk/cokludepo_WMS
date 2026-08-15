using Wms.Modules.Fulfillment.Application;

namespace Wms.Api.Network;

public sealed record NetworkSkuWarehouseResponse(
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
    int? RecentNotFoundCount)
{
    public static NetworkSkuWarehouseResponse From(NetworkSkuWarehouse w) =>
        new(
            w.WarehouseId,
            w.WarehouseCode,
            w.IsOperational,
            w.PhysicalStock,
            w.Allocated,
            w.Atp,
            w.Hold,
            w.Quarantine,
            w.Damaged,
            w.RiskLevel,
            w.RiskScore,
            w.RecentNotFoundCount);
}

public sealed record SkuNetworkResponse(
    Guid SkuId,
    string SkuCode,
    int NetworkPhysicalStock,
    int NetworkAtp,
    int NetworkAllocated,
    IReadOnlyList<NetworkSkuWarehouseResponse> Warehouses)
{
    public static SkuNetworkResponse From(SkuNetworkView view) =>
        new(
            view.SkuId,
            view.SkuCode,
            view.NetworkPhysicalStock,
            view.NetworkAtp,
            view.NetworkAllocated,
            view.Warehouses.Select(NetworkSkuWarehouseResponse.From).ToList());
}

public sealed record NetworkSkuRowResponse(
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
    int? RiskScore)
{
    public static NetworkSkuRowResponse From(NetworkSkuRow row) =>
        new(
            row.SkuId,
            row.SkuCode,
            row.WarehouseId,
            row.WarehouseCode,
            row.IsOperational,
            row.PhysicalStock,
            row.Allocated,
            row.Atp,
            row.Hold,
            row.Quarantine,
            row.Damaged,
            row.RiskLevel,
            row.RiskScore);
}

public sealed record NetworkSkuPageResponse(
    IReadOnlyList<NetworkSkuRowResponse> Rows,
    int Total,
    int Page,
    int PageSize)
{
    public static NetworkSkuPageResponse From(NetworkSkuPage page) =>
        new(
            page.Rows.Select(NetworkSkuRowResponse.From).ToList(),
            page.Total,
            page.Page,
            page.PageSize);
}

public sealed record WarehouseNetworkSkuRowResponse(
    Guid SkuId,
    string SkuCode,
    int PhysicalStock,
    int Allocated,
    int Atp,
    int Hold,
    int Quarantine,
    int Damaged,
    string? RiskLevel,
    int? RiskScore)
{
    public static WarehouseNetworkSkuRowResponse From(WarehouseNetworkSkuRow row) =>
        new(
            row.SkuId,
            row.SkuCode,
            row.PhysicalStock,
            row.Allocated,
            row.Atp,
            row.Hold,
            row.Quarantine,
            row.Damaged,
            row.RiskLevel,
            row.RiskScore);
}

public sealed record WarehouseNetworkResponse(
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
    IReadOnlyList<WarehouseNetworkSkuRowResponse> Skus,
    int Page,
    int PageSize)
{
    public static WarehouseNetworkResponse From(WarehouseNetworkView view) =>
        new(
            view.WarehouseId,
            view.WarehouseCode,
            view.IsOperational,
            view.SkuCount,
            view.PhysicalStock,
            view.Allocated,
            view.Atp,
            view.Hold,
            view.Quarantine,
            view.Damaged,
            view.Skus.Select(WarehouseNetworkSkuRowResponse.From).ToList(),
            view.Page,
            view.PageSize);
}

public sealed record NetworkWarehouseSummaryResponse(
    Guid WarehouseId,
    string Code,
    bool IsOperational,
    int PhysicalStock,
    int Atp,
    int Allocated,
    int Hold,
    int Quarantine,
    int Damaged,
    int SkuCount)
{
    public static NetworkWarehouseSummaryResponse From(NetworkWarehouseSummaryRow row) =>
        new(
            row.WarehouseId,
            row.Code,
            row.IsOperational,
            row.PhysicalStock,
            row.Atp,
            row.Allocated,
            row.Hold,
            row.Quarantine,
            row.Damaged,
            row.SkuCount);
}

public sealed record NetworkSummaryResponse(
    int TotalWarehouses,
    int ActiveWarehouses,
    int PhysicalStock,
    int Atp,
    int Allocated,
    int Hold,
    int Quarantine,
    int Damaged,
    IReadOnlyList<NetworkWarehouseSummaryResponse> Warehouses)
{
    public static NetworkSummaryResponse From(NetworkSummary summary) =>
        new(
            summary.TotalWarehouses,
            summary.ActiveWarehouses,
            summary.PhysicalStock,
            summary.Atp,
            summary.Allocated,
            summary.Hold,
            summary.Quarantine,
            summary.Damaged,
            summary.Warehouses.Select(NetworkWarehouseSummaryResponse.From).ToList());
}

public sealed record AvailabilityRequestLine(Guid SkuId, int Quantity);

public sealed record OrderAvailabilityRequest(IReadOnlyList<AvailabilityRequestLine> Lines);

public sealed record OrderAvailabilityWarehouseResponse(
    Guid WarehouseId,
    string Code,
    bool IsOperational,
    int Atp,
    bool CanSatisfy,
    string? RiskLevel)
{
    public static OrderAvailabilityWarehouseResponse From(OrderAvailabilityWarehouse w) =>
        new(w.WarehouseId, w.Code, w.IsOperational, w.Atp, w.CanSatisfy, w.RiskLevel);
}

public sealed record OrderAvailabilityLineResponse(
    Guid SkuId,
    string SkuCode,
    int RequestedQuantity,
    int NetworkAtp,
    bool IsSatisfiable,
    IReadOnlyList<OrderAvailabilityWarehouseResponse> Warehouses)
{
    public static OrderAvailabilityLineResponse From(OrderAvailabilityLine line) =>
        new(
            line.SkuId,
            line.SkuCode,
            line.RequestedQuantity,
            line.NetworkAtp,
            line.IsSatisfiable,
            line.Warehouses.Select(OrderAvailabilityWarehouseResponse.From).ToList());
}

public sealed record OrderAvailabilityResponse(IReadOnlyList<OrderAvailabilityLineResponse> Lines)
{
    public static OrderAvailabilityResponse From(IReadOnlyList<OrderAvailabilityLine> lines) =>
        new(lines.Select(OrderAvailabilityLineResponse.From).ToList());
}
