using Microsoft.EntityFrameworkCore;
using Npgsql;
using Wms.Modules.Inventory.Application;
using Wms.Modules.Inventory.Application.Accuracy.Reconciliation;
using Wms.Modules.Inventory.Domain;
using Wms.Modules.Inventory.Domain.Accuracy.Scanning;

namespace Wms.Modules.Inventory.Infrastructure.Persistence;

public sealed class InventoryStore(InventoryDbContext db) : IInventoryStore
{
    public async Task<InventoryBalance?> GetBalanceAsync(
        Guid warehouseId,
        Guid skuId,
        Guid locationId,
        InventoryStatus status,
        CancellationToken cancellationToken)
    {
        return await db.InventoryBalances.FirstOrDefaultAsync(
            b => b.WarehouseId == warehouseId && b.SkuId == skuId && b.LocationId == locationId && b.Status == status,
            cancellationToken);
    }

    public async Task<IReadOnlyList<InventoryBalance>> ListBalancesAsync(
        Guid warehouseId,
        Guid? skuId,
        Guid? locationId,
        bool includeEmpty,
        CancellationToken cancellationToken)
    {
        var query = db.InventoryBalances.AsNoTracking().Where(b => b.WarehouseId == warehouseId);

        if (skuId.HasValue)
        {
            query = query.Where(b => b.SkuId == skuId.Value);
        }

        if (locationId.HasValue)
        {
            query = query.Where(b => b.LocationId == locationId.Value);
        }

        if (!includeEmpty)
        {
            query = query.Where(b => b.Quantity > 0 || b.Allocated > 0);
        }

        var result = await query
            .OrderBy(b => b.LocationId)
            .ThenBy(b => b.Status)
            .ToListAsync(cancellationToken);
        return result.AsReadOnly();
    }

    public async Task<List<InventoryBalance>> LockAvailableBalancesAsync(
        Guid warehouseId,
        Guid skuId,
        CancellationToken cancellationToken)
    {
        return await db.InventoryBalances
            .FromSqlRaw(
                """
                SELECT id, sku_id, warehouse_id, location_id, status, quantity, allocated, xmin, created_at, updated_at
                FROM inventory.inventory_balance
                WHERE warehouse_id = {0} AND sku_id = {1} AND status = 'AVAILABLE'
                ORDER BY location_id
                FOR UPDATE
                """,
                warehouseId,
                skuId)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> TryRecordOpeningBalanceAtomicAsync(
        Guid requestId,
        Guid skuId,
        Guid warehouseId,
        Guid locationId,
        InventoryStatus status,
        int quantity,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO inventory.inventory_operation (request_id, operation_type, created_at) VALUES ({0}, 'OpeningBalance', now())",
                requestId);

            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO inventory.inventory_balance (id, sku_id, warehouse_id, location_id, status, quantity, allocated, created_at, updated_at)
                VALUES ({0}, {1}, {2}, {3}, {4}, {5}, 0, now(), now())
                ON CONFLICT (sku_id, warehouse_id, location_id, status)
                DO UPDATE SET quantity = inventory.inventory_balance.quantity + EXCLUDED.quantity,
                              updated_at = now()
                """,
                Guid.NewGuid(),
                skuId,
                warehouseId,
                locationId,
                status.ToString().ToUpperInvariant(),
                quantity);

            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO inventory.inventory_ledger (id, request_id, sku_id, warehouse_id, location_id, status, entry_type, quantity_delta, allocated_delta, occurred_at)
                VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, 0, now())
                """,
                Guid.NewGuid(),
                requestId,
                skuId,
                warehouseId,
                locationId,
                status.ToString().ToUpperInvariant(),
                LedgerEntryType.OpeningBalance.ToString().ToUpperInvariant(),
                quantity);

            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation && exception.ConstraintName == "pk_inventory_operation")
        {
            return false;
        }
    }

    public async Task<bool> OperationExistsAsync(Guid requestId, CancellationToken cancellationToken)
    {
        return await db.Set<InventoryOperation>().AnyAsync(o => o.RequestId == requestId, cancellationToken);
    }

    public async Task AddOperationAsync(Guid requestId, string operationType, CancellationToken cancellationToken)
    {
        db.Set<InventoryOperation>().Add(new InventoryOperation(requestId, operationType));
        await Task.CompletedTask;
    }

    public async Task<InventoryReservation?> GetReservationAsync(Guid reservationId, CancellationToken cancellationToken)
    {
        return await db.InventoryReservations
            .Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.Id == reservationId, cancellationToken);
    }

    public async Task<InventoryReservation?> GetReservationByRequestIdAsync(Guid requestId, CancellationToken cancellationToken)
    {
        return await db.InventoryReservations
            .Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.RequestId == requestId, cancellationToken);
    }

    public async Task AddReservationAsync(InventoryReservation reservation, CancellationToken cancellationToken)
    {
        await db.InventoryReservations.AddAsync(reservation, cancellationToken);
    }

    public async Task AddLedgerEntriesAsync(IEnumerable<InventoryLedgerEntry> entries, CancellationToken cancellationToken)
    {
        await db.InventoryLedgerEntries.AddRangeAsync(entries, cancellationToken);
    }

    public async Task<IReadOnlyList<InventoryLedgerEntry>> ListLedgerAsync(
        Guid? warehouseId,
        Guid? skuId,
        Guid? locationId,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = db.InventoryLedgerEntries.AsNoTracking().AsQueryable();

        if (warehouseId.HasValue)
        {
            query = query.Where(e => e.WarehouseId == warehouseId.Value);
        }

        if (skuId.HasValue)
        {
            query = query.Where(e => e.SkuId == skuId.Value);
        }

        if (locationId.HasValue)
        {
            query = query.Where(e => e.LocationId == locationId.Value);
        }

        var result = await query
            .OrderByDescending(e => e.OccurredAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return result.AsReadOnly();
    }

    public async Task<InventoryMovement?> GetMovementAsync(Guid movementId, CancellationToken cancellationToken)
    {
        return await db.Set<InventoryMovement>().FirstOrDefaultAsync(m => m.Id == movementId, cancellationToken);
    }

    public async Task<InventoryMovement?> GetMovementByRequestIdAsync(Guid requestId, CancellationToken cancellationToken)
    {
        return await db.Set<InventoryMovement>().FirstOrDefaultAsync(m => m.RequestId == requestId, cancellationToken);
    }

    public async Task<IReadOnlyList<InventoryMovement>> ListMovementsAsync(
        Guid? warehouseId,
        Guid? skuId,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = db.Set<InventoryMovement>().AsNoTracking().AsQueryable();

        if (warehouseId.HasValue)
        {
            query = query.Where(m => m.WarehouseId == warehouseId.Value);
        }

        if (skuId.HasValue)
        {
            query = query.Where(m => m.SkuId == skuId.Value);
        }

        var result = await query
            .OrderByDescending(m => m.OccurredAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return result.AsReadOnly();
    }

    public async Task<StoreSaveOutcome> ExecuteMovementAsync(
        InventoryMovement movement,
        IReadOnlyList<InventoryLedgerEntry> ledgerEntries,
        Guid sourceBalanceId,
        Guid? destinationBalanceId,
        int quantity,
        ScanMovementEvidence? evidence,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            try
            {
                await db.Database.ExecuteSqlRawAsync(
                    "INSERT INTO inventory.inventory_operation (request_id, operation_type, created_at) VALUES ({0}, 'Movement', now())",
                    movement.RequestId);
            }
            catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                await transaction.RollbackAsync(cancellationToken);
                return StoreSaveOutcome.DuplicateRequest;
            }

            List<InventoryBalance> locked;
            db.ChangeTracker.Clear();
            if (destinationBalanceId.HasValue)
            {
                locked = await db.InventoryBalances
                    .FromSqlRaw(
                        """
                        SELECT id, sku_id, warehouse_id, location_id, status, quantity, allocated, xmin, created_at, updated_at
                        FROM inventory.inventory_balance
                        WHERE id = {0} OR id = {1}
                        ORDER BY id
                        FOR UPDATE
                        """,
                        sourceBalanceId,
                        destinationBalanceId.Value)
                    .ToListAsync(cancellationToken);
            }
            else
            {
                locked = await db.InventoryBalances
                    .FromSqlRaw(
                        """
                        SELECT id, sku_id, warehouse_id, location_id, status, quantity, allocated, xmin, created_at, updated_at
                        FROM inventory.inventory_balance
                        WHERE id = {0}
                        FOR UPDATE
                        """,
                        sourceBalanceId)
                    .ToListAsync(cancellationToken);
            }

            var source = locked.FirstOrDefault(b => b.Id == sourceBalanceId)
                ?? throw new InventoryBalanceNotFoundException(sourceBalanceId);
            var destination = destinationBalanceId.HasValue
                ? locked.FirstOrDefault(b => b.Id == destinationBalanceId.Value)
                : null;

            if (source.UnallocatedQuantity < quantity)
            {
                throw new InsufficientInventoryException(
                    movement.WarehouseId,
                    movement.SkuId,
                    quantity,
                    source.UnallocatedQuantity);
            }

            source.DecreaseQuantityUnallocated(quantity);

            if (destination is not null)
            {
                destination.IncreaseQuantity(quantity);
            }
            else
            {
                await db.Database.ExecuteSqlRawAsync(
                    """
                    INSERT INTO inventory.inventory_balance (id, sku_id, warehouse_id, location_id, status, quantity, allocated, created_at, updated_at)
                    VALUES ({0}, {1}, {2}, {3}, {4}, {5}, 0, now(), now())
                    ON CONFLICT (sku_id, warehouse_id, location_id, status)
                    DO UPDATE SET quantity = inventory.inventory_balance.quantity + EXCLUDED.quantity,
                                  updated_at = now()
                    """,
                    Guid.NewGuid(),
                    movement.SkuId,
                    movement.WarehouseId,
                    movement.DestinationLocationId ?? movement.SourceLocationId,
                    movement.StatusTo.ToString().ToUpperInvariant(),
                    quantity);
            }

            db.Set<InventoryMovement>().Add(movement);
            if (evidence is not null)
            {
                db.Set<ScanMovementEvidence>().Add(evidence);
            }

            await db.InventoryLedgerEntries.AddRangeAsync(ledgerEntries, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return StoreSaveOutcome.Saved;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<ScanMovementEvidence?> GetScanEvidenceByMovementIdAsync(Guid movementId, CancellationToken cancellationToken)
    {
        return await db.Set<ScanMovementEvidence>()
            .FirstOrDefaultAsync(e => e.MovementId == movementId, cancellationToken);
    }

    public async Task AddAccuracySignalAsync(Domain.Accuracy.InventoryAccuracySignal signal, CancellationToken cancellationToken)
    {
        await db.InventoryAccuracySignals.AddAsync(signal, cancellationToken);
    }

    public async Task<Domain.Accuracy.InventoryAccuracySignal?> GetAccuracySignalByRequestIdAsync(Guid requestId, CancellationToken cancellationToken)
    {
        return await db.InventoryAccuracySignals.FirstOrDefaultAsync(s => s.RequestId == requestId, cancellationToken);
    }

    public async Task<IReadOnlyList<Domain.Accuracy.InventoryAccuracySignal>> ListAccuracySignalsAsync(
        Guid? warehouseId,
        Guid? skuId,
        Guid? locationId,
        Domain.Accuracy.AccuracySignalType? signalType,
        DateTime? from,
        DateTime? to,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = db.InventoryAccuracySignals.AsNoTracking().AsQueryable();

        if (warehouseId.HasValue)
        {
            query = query.Where(s => s.WarehouseId == warehouseId.Value);
        }

        if (skuId.HasValue)
        {
            query = query.Where(s => s.SkuId == skuId.Value);
        }

        if (locationId.HasValue)
        {
            query = query.Where(s => s.LocationId == locationId.Value);
        }

        if (signalType.HasValue)
        {
            query = query.Where(s => s.SignalType == signalType.Value);
        }

        if (from.HasValue)
        {
            query = query.Where(s => s.OccurredAt >= from.Value);
        }

        if (to.HasValue)
        {
            query = query.Where(s => s.OccurredAt <= to.Value);
        }

        var result = await query
            .OrderByDescending(s => s.OccurredAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return result.AsReadOnly();
    }

    public async Task<IReadOnlyList<Application.Accuracy.LocationPhysicalActivity>> GetPhysicalActivityAsync(
        Guid warehouseId,
        Guid skuId,
        CancellationToken cancellationToken)
    {
        var rows = await db.Database.SqlQueryRaw<PhysicalActivityRow>(
            """
            SELECT location_id AS "location_id",
                   (COUNT(DISTINCT CASE WHEN occurred_at >= now() - interval '30 days' THEN COALESCE(movement_id, id) END))::int AS "count30d",
                   (COUNT(DISTINCT CASE WHEN occurred_at >= now() - interval '90 days' THEN COALESCE(movement_id, id) END))::int AS "count90d",
                   (COUNT(DISTINCT CASE WHEN occurred_at >= now() - interval '180 days' THEN COALESCE(movement_id, id) END))::int AS "count180d",
                   MAX(occurred_at) AS "last_at"
            FROM inventory.inventory_ledger
            WHERE warehouse_id = {0} AND sku_id = {1} AND quantity_delta <> 0
            GROUP BY location_id
            """,
            warehouseId,
            skuId)
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new Application.Accuracy.LocationPhysicalActivity(r.LocationId, r.Count30d, r.Count90d, r.Count180d, r.LastAt))
            .ToList()
            .AsReadOnly();
    }

    public async Task<IReadOnlyList<Application.Accuracy.SkuEventCount>> GetWarehouseSkuEventCountsAsync(
        Guid warehouseId,
        CancellationToken cancellationToken)
    {
        var rows = await db.Database.SqlQueryRaw<SkuEventCountRow>(
            """
            SELECT sku_id AS "sku_id",
                   (COUNT(DISTINCT CASE WHEN occurred_at >= now() - interval '180 days' THEN COALESCE(movement_id, id) END))::int AS "count180d"
            FROM inventory.inventory_ledger
            WHERE warehouse_id = {0} AND quantity_delta <> 0
            GROUP BY sku_id
            """,
            warehouseId)
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new Application.Accuracy.SkuEventCount(r.SkuId, r.Count180d))
            .ToList()
            .AsReadOnly();
    }

    public async Task<IReadOnlyList<Application.Accuracy.LocationNotFoundStats>> GetNotFoundStatsAsync(
        Guid warehouseId,
        Guid skuId,
        CancellationToken cancellationToken)
    {
        var rows = await db.Database.SqlQueryRaw<NotFoundStatsRow>(
            """
            SELECT location_id AS "location_id",
                   (COUNT(*) FILTER (WHERE occurred_at >= now() - interval '7 days'))::int AS "count7d",
                   (COUNT(*) FILTER (WHERE occurred_at >= now() - interval '30 days'))::int AS "count30d",
                   MAX(occurred_at) AS "last_at"
            FROM inventory.inventory_accuracy_signal
            WHERE warehouse_id = {0} AND sku_id = {1} AND signal_type = 'PICKNOTFOUND'
            GROUP BY location_id
            """,
            warehouseId,
            skuId)
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new Application.Accuracy.LocationNotFoundStats(r.LocationId, r.Count7d, r.Count30d, r.LastAt))
            .ToList()
            .AsReadOnly();
    }

    public async Task<IReadOnlyList<Application.Accuracy.NotFoundOccurrence>> GetNotFoundOccurrencesAsync(
        Guid warehouseId,
        Guid skuId,
        int limit,
        CancellationToken cancellationToken)
    {
        var rows = await db.Database.SqlQueryRaw<NotFoundOccurrenceRow>(
            """
            SELECT location_id AS "location_id", occurred_at AS "occurred_at"
            FROM inventory.inventory_accuracy_signal
            WHERE warehouse_id = {0} AND sku_id = {1} AND signal_type = 'PICKNOTFOUND'
            ORDER BY occurred_at DESC
            LIMIT {2}
            """,
            warehouseId,
            skuId,
            limit)
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new Application.Accuracy.NotFoundOccurrence(r.LocationId, r.OccurredAt))
            .ToList()
            .AsReadOnly();
    }

    private sealed class PhysicalActivityRow
    {
        public Guid LocationId { get; set; }

        public int Count30d { get; set; }

        public int Count90d { get; set; }

        public int Count180d { get; set; }

        public DateTime? LastAt { get; set; }
    }

    private sealed class SkuEventCountRow
    {
        public Guid SkuId { get; set; }

        public int Count180d { get; set; }
    }

    private sealed class NotFoundStatsRow
    {
        public Guid LocationId { get; set; }

        public int Count7d { get; set; }

        public int Count30d { get; set; }

        public DateTime? LastAt { get; set; }
    }

    private sealed class NotFoundOccurrenceRow
    {
        public Guid LocationId { get; set; }

        public DateTime OccurredAt { get; set; }
    }

    public async Task AddCycleCountTaskAsync(Domain.Accuracy.CycleCounting.CycleCountTask task, CancellationToken cancellationToken)
    {
        await db.Set<Domain.Accuracy.CycleCounting.CycleCountTask>().AddAsync(task, cancellationToken);
    }

    public async Task<Domain.Accuracy.CycleCounting.CycleCountTask?> GetCycleCountTaskAsync(Guid taskId, CancellationToken cancellationToken)
    {
        return await db.Set<Domain.Accuracy.CycleCounting.CycleCountTask>()
            .FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken);
    }

    public async Task<Domain.Accuracy.CycleCounting.CycleCountTask?> GetActiveCycleCountTaskAsync(
        Guid warehouseId,
        Guid skuId,
        Guid locationId,
        CancellationToken cancellationToken)
    {
        return await db.Set<Domain.Accuracy.CycleCounting.CycleCountTask>()
            .FirstOrDefaultAsync(t =>
                t.WarehouseId == warehouseId
                && t.SkuId == skuId
                && t.LocationId == locationId
                && (t.Status == Domain.Accuracy.CycleCounting.CycleCountTaskStatus.Pending
                    || t.Status == Domain.Accuracy.CycleCounting.CycleCountTaskStatus.InProgress),
                cancellationToken);
    }

    public async Task<IReadOnlyList<Domain.Accuracy.CycleCounting.CycleCountTask>> ListCycleCountTasksAsync(
        Guid? warehouseId,
        Domain.Accuracy.CycleCounting.CycleCountTaskStatus? status,
        Domain.Accuracy.CycleCounting.CycleCountPriority? priority,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = db.Set<Domain.Accuracy.CycleCounting.CycleCountTask>().AsNoTracking().AsQueryable();

        if (warehouseId.HasValue)
        {
            query = query.Where(t => t.WarehouseId == warehouseId.Value);
        }

        if (status.HasValue)
        {
            query = query.Where(t => t.Status == status.Value);
        }

        if (priority.HasValue)
        {
            query = query.Where(t => t.Priority == priority.Value);
        }

        var result = await query
            .OrderByDescending(t => t.Priority)
            .ThenBy(t => t.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return result.AsReadOnly();
    }

    public async Task<IReadOnlyList<Domain.Accuracy.CycleCounting.CycleCountTask>> GetCycleCountQueueAsync(
        Guid? warehouseId,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = db.Set<Domain.Accuracy.CycleCounting.CycleCountTask>()
            .AsNoTracking()
            .Where(t => t.Status == Domain.Accuracy.CycleCounting.CycleCountTaskStatus.Pending
                        || t.Status == Domain.Accuracy.CycleCounting.CycleCountTaskStatus.InProgress);

        if (warehouseId.HasValue)
        {
            query = query.Where(t => t.WarehouseId == warehouseId.Value);
        }

        var result = await query
            .OrderByDescending(t => t.Priority)
            .ThenBy(t => t.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return result.AsReadOnly();
    }

    public async Task AddCycleCountResultAsync(Domain.Accuracy.CycleCounting.CycleCountResult result, CancellationToken cancellationToken)
    {
        await db.Set<Domain.Accuracy.CycleCounting.CycleCountResult>().AddAsync(result, cancellationToken);
    }

    public async Task<Domain.Accuracy.CycleCounting.CycleCountResult?> GetCycleCountResultAsync(Guid taskId, CancellationToken cancellationToken)
    {
        return await db.Set<Domain.Accuracy.CycleCounting.CycleCountResult>()
            .FirstOrDefaultAsync(r => r.CycleCountTaskId == taskId, cancellationToken);
    }

    public async Task<DateTime?> GetLatestVerifiedCountAtAsync(
        Guid warehouseId,
        Guid skuId,
        Guid locationId,
        CancellationToken cancellationToken)
    {
        return await db.Database.SqlQueryRaw<DateTime?>(
            """
            SELECT MAX(r.counted_at) AS "Value"
            FROM inventory.cycle_count_result r
            INNER JOIN inventory.cycle_count_task t ON t.id = r.cycle_count_task_id
            WHERE t.warehouse_id = {0} AND t.sku_id = {1} AND t.location_id = {2} AND r.outcome = 'VERIFIED'
            """,
            warehouseId,
            skuId,
            locationId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task AddReconciliationAsync(Domain.Accuracy.Reconciliation.InventoryReconciliation reconciliation, CancellationToken cancellationToken)
    {
        await db.Set<Domain.Accuracy.Reconciliation.InventoryReconciliation>().AddAsync(reconciliation, cancellationToken);
    }

    public async Task<Domain.Accuracy.Reconciliation.InventoryReconciliation?> GetReconciliationAsync(Guid reconciliationId, CancellationToken cancellationToken)
    {
        return await db.Set<Domain.Accuracy.Reconciliation.InventoryReconciliation>()
            .FirstOrDefaultAsync(r => r.Id == reconciliationId, cancellationToken);
    }

    public async Task<Domain.Accuracy.Reconciliation.InventoryReconciliation?> GetReconciliationByResultIdAsync(Guid resultId, CancellationToken cancellationToken)
    {
        return await db.Set<Domain.Accuracy.Reconciliation.InventoryReconciliation>()
            .FirstOrDefaultAsync(r => r.CycleCountResultId == resultId, cancellationToken);
    }

    public async Task<IReadOnlyList<Domain.Accuracy.Reconciliation.InventoryReconciliation>> ListReconciliationsAsync(
        Guid? warehouseId,
        Domain.Accuracy.Reconciliation.ReconciliationStatus? status,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = db.Set<Domain.Accuracy.Reconciliation.InventoryReconciliation>().AsNoTracking().AsQueryable();

        if (warehouseId.HasValue)
        {
            query = query.Where(r => r.WarehouseId == warehouseId.Value);
        }

        if (status.HasValue)
        {
            query = query.Where(r => r.ReconciliationStatus == status.Value);
        }

        var result = await query
            .OrderByDescending(r => r.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return result.AsReadOnly();
    }

    public async Task<Domain.Accuracy.Reconciliation.InventoryAdjustment?> GetAdjustmentByReconciliationIdAsync(
        Guid reconciliationId,
        CancellationToken cancellationToken)
    {
        return await db.Set<Domain.Accuracy.Reconciliation.InventoryAdjustment>()
            .FirstOrDefaultAsync(a => a.ReconciliationId == reconciliationId, cancellationToken);
    }

    public async Task<Application.Accuracy.Reconciliation.ApprovalOutcome> ExecuteReconciliationApprovalAsync(
        Guid reconciliationId,
        Guid approvalRequestId,
        int quantityDelta,
        Domain.Accuracy.Reconciliation.AdjustmentReason reason,
        string? resolvedBy,
        string? resolutionNote,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            try
            {
                await db.Database.ExecuteSqlRawAsync(
                    "INSERT INTO inventory.inventory_operation (request_id, operation_type, created_at) VALUES ({0}, 'ReconciliationApproval', now())",
                    approvalRequestId);
            }
            catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Application.Accuracy.Reconciliation.ApprovalOutcome.AlreadyApproved;
            }

            var reconciliation = await db.Set<Domain.Accuracy.Reconciliation.InventoryReconciliation>()
                .FirstOrDefaultAsync(r => r.Id == reconciliationId, cancellationToken)
                ?? throw new ReconciliationNotFoundException(reconciliationId);

            if (reconciliation.ReconciliationStatus == Domain.Accuracy.Reconciliation.ReconciliationStatus.Approved)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Application.Accuracy.Reconciliation.ApprovalOutcome.AlreadyApproved;
            }

            if (reconciliation.ReconciliationStatus != Domain.Accuracy.Reconciliation.ReconciliationStatus.Open)
            {
                throw new InvalidReconciliationStateException(
                    $"Yalnızca OPEN reconciliation approve edilebilir. Mevcut: {reconciliation.ReconciliationStatus}.");
            }

            db.ChangeTracker.Clear();

            var locked = await db.InventoryBalances
                .FromSqlRaw(
                    """
                    SELECT id, sku_id, warehouse_id, location_id, status, quantity, allocated, xmin, created_at, updated_at
                    FROM inventory.inventory_balance
                    WHERE warehouse_id = {0} AND sku_id = {1} AND location_id = {2} AND status = {3}
                    FOR UPDATE
                    """,
                    reconciliation.WarehouseId,
                    reconciliation.SkuId,
                    reconciliation.LocationId,
                    reconciliation.Status.ToString().ToUpperInvariant())
                .ToListAsync(cancellationToken);

            var balance = locked.SingleOrDefault()
                ?? throw new InventoryBalanceNotFoundException(Guid.Empty);

            if (balance.Quantity != reconciliation.ExpectedQuantity)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Application.Accuracy.Reconciliation.ApprovalOutcome.Stale;
            }

            var newQuantity = balance.Quantity + quantityDelta;
            if (newQuantity < 0 || newQuantity < balance.Allocated)
            {
                throw new AdjustmentConflictException(
                    $"Adjustment allocated invariant'ını bozar: yeni miktar {newQuantity}, allocated {balance.Allocated}.");
            }

            balance.ApplyAdjustment(quantityDelta);

            var freshReconciliation = await db.Set<Domain.Accuracy.Reconciliation.InventoryReconciliation>()
                .FirstAsync(r => r.Id == reconciliationId, cancellationToken);
            freshReconciliation.Approve(resolvedBy, resolutionNote);

            var adjustment = Domain.Accuracy.Reconciliation.InventoryAdjustment.Create(
                reconciliationId,
                approvalRequestId,
                reconciliation.SkuId,
                reconciliation.WarehouseId,
                reconciliation.LocationId,
                reconciliation.Status,
                quantityDelta,
                reason,
                resolvedBy,
                resolutionNote);

            var ledgerEntry = InventoryLedgerEntry.Create(
                approvalRequestId,
                reconciliation.SkuId,
                reconciliation.WarehouseId,
                reconciliation.LocationId,
                reconciliation.Status,
                LedgerEntryType.InventoryAdjustment,
                quantityDelta,
                0);

            var signal = Domain.Accuracy.InventoryAccuracySignal.CreateDiscrepancyConfirmed(
                approvalRequestId,
                Domain.Accuracy.AccuracySourceType.CycleCount,
                reconciliation.SkuId,
                reconciliation.WarehouseId,
                reconciliation.LocationId,
                reconciliationId,
                DateTime.UtcNow,
                balance.Quantity,
                balance.Allocated,
                balance.Available,
                reconciliation.Status);

            db.Set<Domain.Accuracy.Reconciliation.InventoryAdjustment>().Add(adjustment);
            await db.InventoryLedgerEntries.AddAsync(ledgerEntry, cancellationToken);
            await db.InventoryAccuracySignals.AddAsync(signal, cancellationToken);

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Application.Accuracy.Reconciliation.ApprovalOutcome.Applied;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? _transaction;

    public async Task BeginTransactionAsync(CancellationToken cancellationToken)
    {
        _transaction = await db.Database.BeginTransactionAsync(cancellationToken);
    }

    public async Task CommitTransactionAsync(CancellationToken cancellationToken)
    {
        if (_transaction is null)
        {
            return;
        }

        await _transaction.CommitAsync(cancellationToken);
        await _transaction.DisposeAsync();
        _transaction = null;
    }

    public async Task RollbackTransactionAsync(CancellationToken cancellationToken)
    {
        if (_transaction is null)
        {
            return;
        }

        await _transaction.RollbackAsync(cancellationToken);
        await _transaction.DisposeAsync();
        _transaction = null;
    }

    public async Task<StoreSaveOutcome> SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return StoreSaveOutcome.Saved;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
            return StoreSaveOutcome.DuplicateRequest;
        }
    }
}
