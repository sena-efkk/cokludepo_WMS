using Wms.Modules.Inventory.Domain;

namespace Wms.Modules.Inventory.Contracts;

public sealed record AvailabilityInfo(int OnHand, int Allocated, int Available);

public sealed record ReservationCreatedInfo(Guid ReservationId, Guid RequestId, Guid SkuId, int Quantity, IReadOnlyList<ReservationLineInfo> Lines);

public sealed record ReservationLineInfo(Guid ReservationLineId, Guid LocationId, int Quantity);

public sealed record ReservationDetailInfo(
    Guid Id,
    Guid SkuId,
    Guid WarehouseId,
    int Quantity,
    string Status,
    IReadOnlyList<ReservationLineInfo> Lines);

public enum ReserveOrderOutcome
{
    Reserved = 1,
    InsufficientStock = 2,
    AlreadyRecorded = 3,
}

public sealed record ReserveOrderLineInput(Guid SkuId, int Quantity);

public sealed record ReserveOrderResult(ReserveOrderOutcome Outcome, IReadOnlyList<ReservationCreatedInfo> Reservations);

public enum ReceiveInventoryOutcome
{
    Recorded = 1,
    DuplicateRequest = 2,
}

public sealed record ReceiveInventoryCommand(
    Guid RequestId,
    Guid SkuId,
    Guid WarehouseId,
    Guid LocationId,
    string Status,
    int Quantity,
    string? ReferenceType,
    Guid? ReferenceId);

public sealed record ReceiveInventoryResult(ReceiveInventoryOutcome Outcome, Guid RequestId);

public enum ScannedRelocationContractStatus
{
    Completed = 1,
    Rejected = 2,
    DuplicateRequest = 3,
}

public sealed record ScannedRelocationContractCommand(
    Guid RequestId,
    Guid WarehouseId,
    string SourceLocationScan,
    string SkuScan,
    string DestinationLocationScan,
    int Quantity,
    string? DeviceId,
    string? OperatorId);

public sealed record ScannedRelocationContractResult(
    ScannedRelocationContractStatus Status,
    string? RejectionCode,
    string? RejectionReason,
    Guid? MovementId,
    Guid? EvidenceId,
    Guid? SkuId,
    Guid? SourceLocationId,
    Guid? DestinationLocationId,
    int? Quantity);

public sealed record SkuWarehouseAvailability(
    Guid SkuId,
    Guid WarehouseId,
    int PhysicalStock,
    int Allocated,
    int AvailableQuantity,
    int Hold,
    int Quarantine,
    int Damaged)
{
    public int Atp => AvailableQuantity - Allocated;
}

public sealed record SkuLocationBalance(
    Guid LocationId,
    string Status,
    int Quantity,
    int Allocated,
    int Available);

public sealed record WarehouseStockRollup(
    Guid WarehouseId,
    int SkuCount,
    int PhysicalStock,
    int Allocated,
    int AvailableQuantity,
    int Hold,
    int Quarantine,
    int Damaged)
{
    public int Atp => AvailableQuantity - Allocated;
}

public sealed record WarehouseSkuStockRow(
    Guid SkuId,
    int PhysicalStock,
    int Allocated,
    int AvailableQuantity,
    int Hold,
    int Quarantine,
    int Damaged)
{
    public int Atp => AvailableQuantity - Allocated;
}

public sealed record NetworkRiskPair(Guid WarehouseId, Guid SkuId);

public sealed record SkuWarehouseRisk(
    Guid WarehouseId,
    Guid SkuId,
    string RiskLevel,
    int RiskScore,
    int RecentNotFoundCount);

public interface IInventoryContract
{
    Task<AvailabilityInfo> GetAvailabilityAsync(Guid warehouseId, Guid skuId, CancellationToken cancellationToken);

    Task<ReservationCreatedInfo> ReserveAsync(
        Guid requestId,
        Guid skuId,
        Guid warehouseId,
        int quantity,
        string purpose,
        CancellationToken cancellationToken);

    Task<ReserveOrderResult> ReserveOrderAsync(
        Guid requestId,
        Guid warehouseId,
        IReadOnlyList<ReserveOrderLineInput> lines,
        string purpose,
        CancellationToken cancellationToken);

    Task<ReservationDetailInfo?> GetReservationAsync(Guid reservationId, CancellationToken cancellationToken);

    Task ReleaseReservationAsync(Guid reservationId, CancellationToken cancellationToken);

    Task ConsumeReservationAsync(Guid reservationId, CancellationToken cancellationToken);

    Task ReportPickNotFoundAsync(
        Guid requestId,
        Guid skuId,
        Guid warehouseId,
        Guid locationId,
        string? sourceReferenceId,
        CancellationToken cancellationToken);

    Task<ReceiveInventoryResult> ReceiveInventoryAsync(ReceiveInventoryCommand command, CancellationToken cancellationToken);

    Task<ScannedRelocationContractResult> ExecuteScannedRelocationAsync(
        ScannedRelocationContractCommand command,
        CancellationToken cancellationToken);

    Task<SkuWarehouseAvailability?> GetWarehouseSkuAvailabilityAsync(Guid warehouseId, Guid skuId, CancellationToken cancellationToken);

    Task<IReadOnlyList<SkuWarehouseAvailability>> ListSkuWarehouseAvailabilityAsync(Guid skuId, CancellationToken cancellationToken);

    Task<IReadOnlyList<SkuLocationBalance>> ListSkuLocationBalancesAsync(Guid warehouseId, Guid skuId, CancellationToken cancellationToken);

    Task<IReadOnlyList<WarehouseStockRollup>> ListWarehouseStockRollupsAsync(CancellationToken cancellationToken);

    Task<(IReadOnlyList<WarehouseSkuStockRow> Rows, int Total)> ListWarehouseSkuRowsAsync(
        Guid warehouseId,
        int skip,
        int take,
        CancellationToken cancellationToken);

    Task<(IReadOnlyList<SkuWarehouseAvailability> Rows, int Total)> ListSkuWarehousePageAsync(
        Guid? warehouseId,
        IReadOnlyList<Guid>? skuIds,
        bool? hasStock,
        bool? hasAtp,
        string? sort,
        int skip,
        int take,
        CancellationToken cancellationToken);

    Task<SkuWarehouseRisk?> GetWarehouseSkuRiskAsync(Guid warehouseId, Guid skuId, CancellationToken cancellationToken);

    Task<IReadOnlyList<SkuWarehouseRisk>> ListSkuWarehouseRiskBatchAsync(
        IReadOnlyList<NetworkRiskPair> pairs,
        CancellationToken cancellationToken);
}
