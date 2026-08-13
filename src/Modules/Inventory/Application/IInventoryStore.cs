using Wms.Modules.Inventory.Domain;
using Wms.Modules.Inventory.Domain.Accuracy;
using Wms.Modules.Inventory.Domain.Accuracy.Scanning;

namespace Wms.Modules.Inventory.Application;

public enum StoreSaveOutcome
{
    Saved = 1,
    DuplicateRequest = 2,
}

public interface IInventoryStore
{
    Task<InventoryBalance?> GetBalanceAsync(Guid warehouseId, Guid skuId, Guid locationId, InventoryStatus status, CancellationToken cancellationToken);

    Task<IReadOnlyList<InventoryBalance>> ListBalancesAsync(Guid warehouseId, Guid? skuId, Guid? locationId, bool includeEmpty, CancellationToken cancellationToken);

    Task<List<InventoryBalance>> LockAvailableBalancesAsync(Guid warehouseId, Guid skuId, CancellationToken cancellationToken);

    Task<bool> TryRecordOpeningBalanceAtomicAsync(
        Guid requestId,
        Guid skuId,
        Guid warehouseId,
        Guid locationId,
        InventoryStatus status,
        int quantity,
        CancellationToken cancellationToken);

    Task<bool> OperationExistsAsync(Guid requestId, CancellationToken cancellationToken);

    Task AddOperationAsync(Guid requestId, string operationType, CancellationToken cancellationToken);

    Task<InventoryReservation?> GetReservationAsync(Guid reservationId, CancellationToken cancellationToken);

    Task<InventoryReservation?> GetReservationByRequestIdAsync(Guid requestId, CancellationToken cancellationToken);

    Task AddReservationAsync(InventoryReservation reservation, CancellationToken cancellationToken);

    Task AddLedgerEntriesAsync(IEnumerable<InventoryLedgerEntry> entries, CancellationToken cancellationToken);

    Task<IReadOnlyList<InventoryLedgerEntry>> ListLedgerAsync(Guid? warehouseId, Guid? skuId, Guid? locationId, int limit, CancellationToken cancellationToken);

    Task<InventoryMovement?> GetMovementAsync(Guid movementId, CancellationToken cancellationToken);

    Task<InventoryMovement?> GetMovementByRequestIdAsync(Guid requestId, CancellationToken cancellationToken);

    Task<IReadOnlyList<InventoryMovement>> ListMovementsAsync(Guid? warehouseId, Guid? skuId, int limit, CancellationToken cancellationToken);

    Task<StoreSaveOutcome> ExecuteMovementAsync(
        InventoryMovement movement,
        IReadOnlyList<InventoryLedgerEntry> ledgerEntries,
        Guid sourceBalanceId,
        Guid? destinationBalanceId,
        int quantity,
        ScanMovementEvidence? evidence,
        CancellationToken cancellationToken);

    Task<ScanMovementEvidence?> GetScanEvidenceByMovementIdAsync(Guid movementId, CancellationToken cancellationToken);

    Task AddAccuracySignalAsync(InventoryAccuracySignal signal, CancellationToken cancellationToken);

    Task<InventoryAccuracySignal?> GetAccuracySignalByRequestIdAsync(Guid requestId, CancellationToken cancellationToken);

    Task<IReadOnlyList<InventoryAccuracySignal>> ListAccuracySignalsAsync(
        Guid? warehouseId,
        Guid? skuId,
        Guid? locationId,
        AccuracySignalType? signalType,
        DateTime? from,
        DateTime? to,
        int limit,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Accuracy.LocationPhysicalActivity>> GetPhysicalActivityAsync(
        Guid warehouseId,
        Guid skuId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Accuracy.SkuEventCount>> GetWarehouseSkuEventCountsAsync(
        Guid warehouseId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Accuracy.LocationNotFoundStats>> GetNotFoundStatsAsync(
        Guid warehouseId,
        Guid skuId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Accuracy.NotFoundOccurrence>> GetNotFoundOccurrencesAsync(
        Guid warehouseId,
        Guid skuId,
        int limit,
        CancellationToken cancellationToken);

    Task AddCycleCountTaskAsync(Domain.Accuracy.CycleCounting.CycleCountTask task, CancellationToken cancellationToken);

    Task<Domain.Accuracy.CycleCounting.CycleCountTask?> GetCycleCountTaskAsync(Guid taskId, CancellationToken cancellationToken);

    Task<Domain.Accuracy.CycleCounting.CycleCountTask?> GetActiveCycleCountTaskAsync(
        Guid warehouseId,
        Guid skuId,
        Guid locationId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Domain.Accuracy.CycleCounting.CycleCountTask>> ListCycleCountTasksAsync(
        Guid? warehouseId,
        Domain.Accuracy.CycleCounting.CycleCountTaskStatus? status,
        Domain.Accuracy.CycleCounting.CycleCountPriority? priority,
        int limit,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Domain.Accuracy.CycleCounting.CycleCountTask>> GetCycleCountQueueAsync(
        Guid? warehouseId,
        int limit,
        CancellationToken cancellationToken);

    Task AddCycleCountResultAsync(Domain.Accuracy.CycleCounting.CycleCountResult result, CancellationToken cancellationToken);

    Task<Domain.Accuracy.CycleCounting.CycleCountResult?> GetCycleCountResultAsync(Guid taskId, CancellationToken cancellationToken);

    Task<DateTime?> GetLatestVerifiedCountAtAsync(
        Guid warehouseId,
        Guid skuId,
        Guid locationId,
        CancellationToken cancellationToken);

    Task AddReconciliationAsync(Domain.Accuracy.Reconciliation.InventoryReconciliation reconciliation, CancellationToken cancellationToken);

    Task<Domain.Accuracy.Reconciliation.InventoryReconciliation?> GetReconciliationAsync(Guid reconciliationId, CancellationToken cancellationToken);

    Task<Domain.Accuracy.Reconciliation.InventoryReconciliation?> GetReconciliationByResultIdAsync(Guid resultId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Domain.Accuracy.Reconciliation.InventoryReconciliation>> ListReconciliationsAsync(
        Guid? warehouseId,
        Domain.Accuracy.Reconciliation.ReconciliationStatus? status,
        int limit,
        CancellationToken cancellationToken);

    Task<Domain.Accuracy.Reconciliation.InventoryAdjustment?> GetAdjustmentByReconciliationIdAsync(
        Guid reconciliationId,
        CancellationToken cancellationToken);

    Task<Accuracy.Reconciliation.ApprovalOutcome> ExecuteReconciliationApprovalAsync(
        Guid reconciliationId,
        Guid approvalRequestId,
        int quantityDelta,
        Domain.Accuracy.Reconciliation.AdjustmentReason reason,
        string? resolvedBy,
        string? resolutionNote,
        CancellationToken cancellationToken);

    Task BeginTransactionAsync(CancellationToken cancellationToken);

    Task CommitTransactionAsync(CancellationToken cancellationToken);

    Task RollbackTransactionAsync(CancellationToken cancellationToken);

    Task<StoreSaveOutcome> SaveChangesAsync(CancellationToken cancellationToken);
}
