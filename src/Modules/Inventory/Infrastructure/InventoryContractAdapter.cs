using Wms.Modules.Inventory.Application;
using Wms.Modules.Inventory.Application.Accuracy;
using Wms.Modules.Inventory.Application.Accuracy.Scanning;
using Wms.Modules.Inventory.Contracts;
using Wms.Modules.Inventory.Domain;
using Wms.Modules.Inventory.Domain.Accuracy;
using Wms.Integration.Telemetry;

namespace Wms.Modules.Inventory.Infrastructure;

public sealed class InventoryContractAdapter(
    IInventoryStore store,
    Reserve reserve,
    ReserveOrder reserveOrder,
    GetReservationById getReservationById,
    ReleaseReservation releaseReservation,
    ConsumeReservation consumeReservation,
    GetWarehouseSkuSummary summary,
    ReportPickNotFound reportPickNotFound,
    ReceiveInventory receiveInventory,
    ExecuteScannedRelocation executeScannedRelocation,
    ListRiskAssessments listRiskAssessments) : IInventoryContract
{
    public async Task<AvailabilityInfo> GetAvailabilityAsync(Guid warehouseId, Guid skuId, CancellationToken cancellationToken)
    {
        var result = await summary.Handle(warehouseId, skuId, cancellationToken);
        return new AvailabilityInfo(result.OnHand, result.Allocated, result.Available);
    }

    public async Task<ReservationCreatedInfo> ReserveAsync(
        Guid requestId,
        Guid skuId,
        Guid warehouseId,
        int quantity,
        string purpose,
        CancellationToken cancellationToken)
    {
        try
        {
            var reservation = await reserve.Handle(
                new ReserveCommand(requestId, skuId, warehouseId, quantity, purpose),
                cancellationToken);
            WmsMetrics.InventoryReservationsTotal.Add(1);
            return new ReservationCreatedInfo(
                reservation.Id,
                reservation.RequestId,
                reservation.SkuId,
                reservation.RequestedQuantity,
                reservation.Lines.Select(l => new ReservationLineInfo(l.Id, l.LocationId, l.Quantity)).ToList());
        }
        catch (InsufficientInventoryException)
        {
            WmsMetrics.InventoryReservationFailuresTotal.Add(1);
            throw;
        }
    }

    public async Task<Contracts.ReserveOrderResult> ReserveOrderAsync(
        Guid requestId,
        Guid warehouseId,
        IReadOnlyList<Contracts.ReserveOrderLineInput> lines,
        string purpose,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await reserveOrder.Handle(
                new Application.ReserveOrderCommand(
                    requestId,
                    warehouseId,
                    lines.Select(l => new Application.ReserveOrderLineInput(l.SkuId, l.Quantity)).ToList(),
                    purpose),
                cancellationToken);

            return new Contracts.ReserveOrderResult(
                result.Outcome switch
                {
                    Application.ReserveOrderOutcome.Reserved => Contracts.ReserveOrderOutcome.Reserved,
                    Application.ReserveOrderOutcome.InsufficientStock => Contracts.ReserveOrderOutcome.InsufficientStock,
                    _ => Contracts.ReserveOrderOutcome.AlreadyRecorded,
                },
                result.Reservations
                    .Select(r => new ReservationCreatedInfo(
                        r.Id,
                        r.RequestId,
                        r.SkuId,
                        r.RequestedQuantity,
                        r.Lines.Select(l => new ReservationLineInfo(l.Id, l.LocationId, l.Quantity)).ToList()))
                    .ToList());
        }
        catch (InsufficientInventoryException)
        {
            return new Contracts.ReserveOrderResult(Contracts.ReserveOrderOutcome.InsufficientStock, []);
        }
    }

    public async Task<ReservationDetailInfo?> GetReservationAsync(Guid reservationId, CancellationToken cancellationToken)
    {
        var reservation = await getReservationById.Handle(reservationId, cancellationToken);
        if (reservation is null)
        {
            return null;
        }

        return new ReservationDetailInfo(
            reservation.Id,
            reservation.SkuId,
            reservation.WarehouseId,
            reservation.RequestedQuantity,
            reservation.Status.ToString().ToUpperInvariant(),
            reservation.Lines.Select(l => new ReservationLineInfo(l.Id, l.LocationId, l.Quantity)).ToList());
    }

    public async Task ReleaseReservationAsync(Guid reservationId, CancellationToken cancellationToken)
    {
        await releaseReservation.Handle(reservationId, cancellationToken);
        WmsMetrics.InventoryMovementsTotal.Add(1);
    }

    public async Task ConsumeReservationAsync(Guid reservationId, CancellationToken cancellationToken)
    {
        await consumeReservation.Handle(reservationId, cancellationToken);
        WmsMetrics.InventoryMovementsTotal.Add(1);
    }

    public async Task ReportPickNotFoundAsync(
        Guid requestId,
        Guid skuId,
        Guid warehouseId,
        Guid locationId,
        string? sourceReferenceId,
        CancellationToken cancellationToken)
    {
        await reportPickNotFound.Handle(
            new ReportPickNotFoundCommand(
                requestId,
                skuId,
                warehouseId,
                locationId,
                AccuracySourceType.Pick,
                string.IsNullOrWhiteSpace(sourceReferenceId) ? null : Guid.Parse(sourceReferenceId),
                null),
            cancellationToken);
        WmsMetrics.PickNotFoundTotal.Add(1);
    }

    public async Task<Contracts.ReceiveInventoryResult> ReceiveInventoryAsync(
        Contracts.ReceiveInventoryCommand command,
        CancellationToken cancellationToken)
    {
        var result = await receiveInventory.Handle(
            new Application.ReceiveInventoryCommand(
                command.RequestId,
                command.SkuId,
                command.WarehouseId,
                command.LocationId,
                Enum.Parse<InventoryStatus>(command.Status, ignoreCase: true),
                command.Quantity,
                command.ReferenceType,
                command.ReferenceId),
            cancellationToken);

        WmsMetrics.InventoryReceivesTotal.Add(1);

        return new Contracts.ReceiveInventoryResult(
            result.Outcome == Application.ReceiveInventoryOutcome.Recorded
                ? Contracts.ReceiveInventoryOutcome.Recorded
                : Contracts.ReceiveInventoryOutcome.DuplicateRequest,
            result.RequestId);
    }

    public async Task<ScannedRelocationContractResult> ExecuteScannedRelocationAsync(
        ScannedRelocationContractCommand command,
        CancellationToken cancellationToken)
    {
        var result = await executeScannedRelocation.Handle(
            new Application.Accuracy.Scanning.ScannedRelocationCommand(
                command.RequestId,
                command.WarehouseId,
                command.SourceLocationScan,
                command.SkuScan,
                command.DestinationLocationScan,
                command.Quantity,
                command.DeviceId,
                command.OperatorId),
            cancellationToken);

        return new ScannedRelocationContractResult(
            result.Status switch
            {
                Application.Accuracy.Scanning.ScannedRelocationStatus.Completed => ScannedRelocationContractStatus.Completed,
                Application.Accuracy.Scanning.ScannedRelocationStatus.Rejected => ScannedRelocationContractStatus.Rejected,
                _ => ScannedRelocationContractStatus.DuplicateRequest,
            },
            result.RejectionCode?.ToString(),
            result.RejectionReason,
            result.MovementId,
            result.EvidenceId,
            result.SkuId,
            result.SourceLocationId,
            result.DestinationLocationId,
            result.Quantity);
    }

    public async Task<SkuWarehouseAvailability?> GetWarehouseSkuAvailabilityAsync(
        Guid warehouseId,
        Guid skuId,
        CancellationToken cancellationToken)
    {
        var row = await store.GetSkuWarehouseAvailabilityRowAsync(warehouseId, skuId, cancellationToken);
        return row is null ? null : MapAvailability(row);
    }

    public async Task<IReadOnlyList<SkuWarehouseAvailability>> ListSkuWarehouseAvailabilityAsync(
        Guid skuId,
        CancellationToken cancellationToken)
    {
        var rows = await store.ListSkuWarehouseAvailabilityRowsAsync(skuId, cancellationToken);
        return rows.Select(MapAvailability).ToList();
    }

    public async Task<IReadOnlyList<SkuLocationBalance>> ListSkuLocationBalancesAsync(
        Guid warehouseId,
        Guid skuId,
        CancellationToken cancellationToken)
    {
        var rows = await store.ListSkuLocationBalanceRowsAsync(warehouseId, skuId, cancellationToken);
        return rows
            .Select(r => new SkuLocationBalance(r.LocationId, r.Status, r.Quantity, r.Allocated, r.Available))
            .ToList();
    }

    public async Task<IReadOnlyList<WarehouseStockRollup>> ListWarehouseStockRollupsAsync(
        CancellationToken cancellationToken)
    {
        var rows = await store.ListWarehouseStockRollupRowsAsync(cancellationToken);
        return rows
            .Select(r => new WarehouseStockRollup(
                r.WarehouseId,
                r.SkuCount,
                r.PhysicalStock,
                r.Allocated,
                r.AvailableQuantity,
                r.Hold,
                r.Quarantine,
                r.Damaged))
            .ToList();
    }

    public async Task<(IReadOnlyList<WarehouseSkuStockRow> Rows, int Total)> ListWarehouseSkuRowsAsync(
        Guid warehouseId,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        var (rows, total) = await store.ListWarehouseSkuRowsAsync(warehouseId, skip, take, cancellationToken);
        return (rows
            .Select(r => new WarehouseSkuStockRow(
                r.SkuId,
                r.PhysicalStock,
                r.Allocated,
                r.AvailableQuantity,
                r.Hold,
                r.Quarantine,
                r.Damaged))
            .ToList(), total);
    }

    public async Task<(IReadOnlyList<SkuWarehouseAvailability> Rows, int Total)> ListSkuWarehousePageAsync(
        Guid? warehouseId,
        IReadOnlyList<Guid>? skuIds,
        bool? hasStock,
        bool? hasAtp,
        string? sort,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        var (rows, total) = await store.ListSkuWarehousePageRowsAsync(
            warehouseId,
            skuIds,
            hasStock,
            hasAtp,
            sort,
            skip,
            take,
            cancellationToken);
        return (rows.Select(MapAvailability).ToList(), total);
    }

    public async Task<SkuWarehouseRisk?> GetWarehouseSkuRiskAsync(
        Guid warehouseId,
        Guid skuId,
        CancellationToken cancellationToken)
    {
        var assessments = await listRiskAssessments.Handle(warehouseId, skuId, null, null, 10_000, cancellationToken);
        var relevant = assessments.Where(a => a.SkuId == skuId).ToList();
        if (relevant.Count == 0)
        {
            return null;
        }

        return BuildRisk(warehouseId, skuId, relevant);
    }

    public async Task<IReadOnlyList<SkuWarehouseRisk>> ListSkuWarehouseRiskBatchAsync(
        IReadOnlyList<NetworkRiskPair> pairs,
        CancellationToken cancellationToken)
    {
        var results = new List<SkuWarehouseRisk>();

        foreach (var warehouseGroup in pairs.GroupBy(p => p.WarehouseId))
        {
            var assessments = await listRiskAssessments.Handle(warehouseGroup.Key, null, null, null, 100_000, cancellationToken);
            foreach (var pair in warehouseGroup)
            {
                var relevant = assessments.Where(a => a.SkuId == pair.SkuId).ToList();
                if (relevant.Count > 0)
                {
                    results.Add(BuildRisk(pair.WarehouseId, pair.SkuId, relevant));
                }
            }
        }

        return results;
    }

    private static SkuWarehouseRisk BuildRisk(
        Guid warehouseId,
        Guid skuId,
        IReadOnlyList<LocationRiskAssessment> assessments)
    {
        var maxScore = assessments.Max(a => a.RiskScore);
        var maxLevel = assessments.Max(a => a.RiskLevel);
        var recentNotFound = assessments.Sum(a => a.NotFoundCount30d);
        return new SkuWarehouseRisk(
            warehouseId,
            skuId,
            maxLevel.ToString().ToUpperInvariant(),
            maxScore,
            recentNotFound);
    }

    private static SkuWarehouseAvailability MapAvailability(SkuWarehouseAvailabilityView row) =>
        new(
            row.SkuId,
            row.WarehouseId,
            row.PhysicalStock,
            row.Allocated,
            row.AvailableQuantity,
            row.Hold,
            row.Quarantine,
            row.Damaged);
}
