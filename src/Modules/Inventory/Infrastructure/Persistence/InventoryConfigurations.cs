using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Modules.Inventory.Domain;
using Wms.Modules.Inventory.Domain.Accuracy;

namespace Wms.Modules.Inventory.Infrastructure.Persistence;

public sealed class InventoryBalanceConfiguration : IEntityTypeConfiguration<InventoryBalance>
{
    public void Configure(EntityTypeBuilder<InventoryBalance> builder)
    {
        builder.ToTable("inventory_balance", table =>
        {
            table.HasCheckConstraint("ck_inventory_balance_quantity_non_negative", "quantity >= 0");
            table.HasCheckConstraint("ck_inventory_balance_allocated_non_negative", "allocated >= 0");
            table.HasCheckConstraint("ck_inventory_balance_allocated_not_exceeds_quantity", "allocated <= quantity");
            table.HasCheckConstraint("ck_inventory_balance_allocated_only_available", "status = 'AVAILABLE' OR allocated = 0");
        });

        builder.HasKey(b => b.Id);

        builder.Property(b => b.Status)
            .HasConversion(
                value => value.ToString().ToUpperInvariant(),
                value => Enum.Parse<InventoryStatus>(value, ignoreCase: true))
            .HasMaxLength(16)
            .IsRequired();
        builder.Property(b => b.Quantity).IsRequired();
        builder.Property(b => b.Allocated).IsRequired();
        builder.Property(b => b.CreatedAt).HasColumnType("timestamptz").IsRequired();
        builder.Property(b => b.UpdatedAt).HasColumnType("timestamptz").IsRequired();

        builder.Property(b => b.Version)
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .IsRowVersion();

        builder.HasIndex(b => new { b.SkuId, b.WarehouseId, b.LocationId, b.Status }).IsUnique();
        builder.HasIndex(b => new { b.WarehouseId, b.SkuId, b.Status });
        builder.HasIndex(b => new { b.WarehouseId, b.LocationId });
    }
}

public sealed class InventoryReservationConfiguration : IEntityTypeConfiguration<InventoryReservation>
{
    public void Configure(EntityTypeBuilder<InventoryReservation> builder)
    {
        builder.ToTable("inventory_reservation", table =>
            table.HasCheckConstraint("ck_inventory_reservation_requested_positive", "requested_quantity > 0"));

        builder.HasKey(r => r.Id);

        builder.Property(r => r.RequestId).IsRequired();
        builder.Property(r => r.RequestedQuantity).IsRequired();
        builder.Property(r => r.Status)
            .HasConversion(
                value => value.ToString().ToUpperInvariant(),
                value => Enum.Parse<ReservationStatus>(value, ignoreCase: true))
            .HasMaxLength(16)
            .IsRequired();
        builder.Property(r => r.CreatedAt).HasColumnType("timestamptz").IsRequired();
        builder.Property(r => r.UpdatedAt).HasColumnType("timestamptz").IsRequired();

        builder.HasIndex(r => r.RequestId).IsUnique();
        builder.HasIndex(r => new { r.WarehouseId, r.SkuId });

        builder.HasMany(r => r.Lines)
            .WithOne()
            .HasForeignKey(l => l.ReservationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ReservationLineConfiguration : IEntityTypeConfiguration<ReservationLine>
{
    public void Configure(EntityTypeBuilder<ReservationLine> builder)
    {
        builder.ToTable("inventory_reservation_line", table =>
            table.HasCheckConstraint("ck_inventory_reservation_line_quantity_positive", "quantity > 0"));

        builder.HasKey(l => l.Id);

        builder.Property(l => l.Quantity).IsRequired();

        builder.HasIndex(l => l.ReservationId);
        builder.HasIndex(l => l.LocationId);
    }
}

public sealed class InventoryLedgerEntryConfiguration : IEntityTypeConfiguration<InventoryLedgerEntry>
{
    public void Configure(EntityTypeBuilder<InventoryLedgerEntry> builder)
    {
        builder.ToTable("inventory_ledger");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Status)
            .HasConversion(
                value => value.ToString().ToUpperInvariant(),
                value => Enum.Parse<InventoryStatus>(value, ignoreCase: true))
            .HasMaxLength(16)
            .IsRequired();
        builder.Property(e => e.EntryType)
            .HasConversion(
                value => value.ToString().ToUpperInvariant(),
                value => Enum.Parse<LedgerEntryType>(value, ignoreCase: true))
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(e => e.QuantityDelta).IsRequired();
        builder.Property(e => e.AllocatedDelta).IsRequired();
        builder.Property(e => e.OccurredAt).HasColumnType("timestamptz").IsRequired();
        builder.Property(e => e.ReferenceType).HasMaxLength(64);
        builder.Property(e => e.ReferenceId);

        builder.HasIndex(e => new { e.WarehouseId, e.SkuId });
        builder.HasIndex(e => e.WarehouseId);
        builder.HasIndex(e => e.RequestId);
        builder.HasIndex(e => e.MovementId);
        builder.HasIndex(e => new { e.ReferenceType, e.ReferenceId });
    }
}

public sealed class InventoryMovementConfiguration : IEntityTypeConfiguration<InventoryMovement>
{
    public void Configure(EntityTypeBuilder<InventoryMovement> builder)
    {
        builder.ToTable("inventory_movement", table =>
            table.HasCheckConstraint("ck_inventory_movement_quantity_positive", "quantity > 0"));

        builder.HasKey(m => m.Id);

        builder.Property(m => m.Type)
            .HasConversion(
                value => value.ToString().ToUpperInvariant(),
                value => Enum.Parse<MovementType>(value, ignoreCase: true))
            .HasMaxLength(20)
            .IsRequired();
        builder.Property(m => m.StatusFrom)
            .HasConversion(
                value => value.ToString().ToUpperInvariant(),
                value => Enum.Parse<InventoryStatus>(value, ignoreCase: true))
            .HasMaxLength(16)
            .IsRequired();
        builder.Property(m => m.StatusTo)
            .HasConversion(
                value => value.ToString().ToUpperInvariant(),
                value => Enum.Parse<InventoryStatus>(value, ignoreCase: true))
            .HasMaxLength(16)
            .IsRequired();
        builder.Property(m => m.Quantity).IsRequired();
        builder.Property(m => m.OccurredAt).HasColumnType("timestamptz").IsRequired();

        builder.HasIndex(m => m.RequestId).IsUnique();
        builder.HasIndex(m => new { m.WarehouseId, m.SkuId });
    }
}

public sealed class CycleCountTaskConfiguration : IEntityTypeConfiguration<Domain.Accuracy.CycleCounting.CycleCountTask>
{
    public void Configure(EntityTypeBuilder<Domain.Accuracy.CycleCounting.CycleCountTask> builder)
    {
        builder.ToTable("cycle_count_task", table =>
        {
            table.HasCheckConstraint("ck_cycle_count_task_risk_score_non_negative", "risk_score_at_creation >= 0");
            table.HasCheckConstraint("ck_cycle_count_task_expected_non_negative", "expected_quantity >= 0 AND expected_allocated >= 0");
        });

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Reason)
            .HasConversion(
                value => value.ToString().ToUpperInvariant(),
                value => Enum.Parse<Domain.Accuracy.CycleCounting.CycleCountReason>(value, ignoreCase: true))
            .HasMaxLength(24)
            .IsRequired();
        builder.Property(t => t.Priority)
            .HasConversion(
                value => value.ToString().ToUpperInvariant(),
                value => Enum.Parse<Domain.Accuracy.CycleCounting.CycleCountPriority>(value, ignoreCase: true))
            .HasMaxLength(16)
            .IsRequired();
        builder.Property(t => t.Status)
            .HasConversion(
                value => value.ToString().ToUpperInvariant(),
                value => Enum.Parse<Domain.Accuracy.CycleCounting.CycleCountTaskStatus>(value, ignoreCase: true))
            .HasMaxLength(16)
            .IsRequired();
        builder.Property(t => t.ExpectedStatus)
            .HasConversion(
                value => value.HasValue ? value.Value.ToString().ToUpperInvariant() : null,
                value => string.IsNullOrEmpty(value) ? (InventoryStatus?)null : Enum.Parse<InventoryStatus>(value, ignoreCase: true))
            .HasMaxLength(16);
        builder.Property(t => t.Evidence).HasMaxLength(2000).IsRequired();
        builder.Property(t => t.RiskScoreAtCreation).IsRequired();
        builder.Property(t => t.AssignedTo).HasMaxLength(100);
        builder.Property(t => t.CreatedAt).HasColumnType("timestamptz").IsRequired();
        builder.Property(t => t.DueAt).HasColumnType("timestamptz");
        builder.Property(t => t.StartedAt).HasColumnType("timestamptz");
        builder.Property(t => t.CompletedAt).HasColumnType("timestamptz");

        builder.HasIndex(t => new { t.SkuId, t.WarehouseId, t.LocationId })
            .IsUnique()
            .HasFilter("status IN ('PENDING','INPROGRESS')");
        builder.HasIndex(t => new { t.WarehouseId, t.Status, t.Priority });
        builder.HasIndex(t => t.CreatedAt);
    }
}

public sealed class CycleCountResultConfiguration : IEntityTypeConfiguration<Domain.Accuracy.CycleCounting.CycleCountResult>
{
    public void Configure(EntityTypeBuilder<Domain.Accuracy.CycleCounting.CycleCountResult> builder)
    {
        builder.ToTable("cycle_count_result", table =>
        {
            table.HasCheckConstraint("ck_cycle_count_result_counted_non_negative", "counted_quantity >= 0");
            table.HasCheckConstraint("ck_cycle_count_result_expected_non_negative", "expected_quantity >= 0 AND expected_allocated >= 0");
        });

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Outcome)
            .HasConversion(
                value => value.ToString().ToUpperInvariant(),
                value => Enum.Parse<Domain.Accuracy.CycleCounting.CountOutcome>(value, ignoreCase: true))
            .HasMaxLength(24)
            .IsRequired();
        builder.Property(r => r.ExpectedStatus)
            .HasConversion(
                value => value.ToString().ToUpperInvariant(),
                value => Enum.Parse<InventoryStatus>(value, ignoreCase: true))
            .HasMaxLength(16)
            .IsRequired();
        builder.Property(r => r.CountedBy).HasMaxLength(100);
        builder.Property(r => r.CountedAt).HasColumnType("timestamptz").IsRequired();
        builder.Property(r => r.CountedQuantity).IsRequired();
        builder.Property(r => r.ExpectedQuantity).IsRequired();
        builder.Property(r => r.ExpectedAllocated).IsRequired();
        builder.Property(r => r.Variance).IsRequired();

        builder.HasIndex(r => r.CycleCountTaskId).IsUnique();
    }
}

public sealed class InventoryReconciliationConfiguration : IEntityTypeConfiguration<Domain.Accuracy.Reconciliation.InventoryReconciliation>
{
    public void Configure(EntityTypeBuilder<Domain.Accuracy.Reconciliation.InventoryReconciliation> builder)
    {
        builder.ToTable("inventory_reconciliation", table =>
        {
            table.HasCheckConstraint("ck_inventory_reconciliation_variance_nonzero", "variance <> 0");
            table.HasCheckConstraint("ck_inventory_reconciliation_quantities_non_negative", "expected_quantity >= 0 AND counted_quantity >= 0");
        });

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Status)
            .HasConversion(
                value => value.ToString().ToUpperInvariant(),
                value => Enum.Parse<InventoryStatus>(value, ignoreCase: true))
            .HasMaxLength(16)
            .IsRequired();
        builder.Property(r => r.Reason)
            .HasConversion(
                value => value.ToString().ToUpperInvariant(),
                value => Enum.Parse<Domain.Accuracy.Reconciliation.AdjustmentReason>(value, ignoreCase: true))
            .HasMaxLength(24)
            .IsRequired();
        builder.Property(r => r.ReconciliationStatus)
            .HasConversion(
                value => value.ToString().ToUpperInvariant(),
                value => Enum.Parse<Domain.Accuracy.Reconciliation.ReconciliationStatus>(value, ignoreCase: true))
            .HasMaxLength(16)
            .IsRequired();
        builder.Property(r => r.ExpectedQuantity).IsRequired();
        builder.Property(r => r.CountedQuantity).IsRequired();
        builder.Property(r => r.Variance).IsRequired();
        builder.Property(r => r.IsLargeVariance).IsRequired();
        builder.Property(r => r.ResolutionNote).HasMaxLength(1000).IsRequired();
        builder.Property(r => r.ResolvedBy).HasMaxLength(100);
        builder.Property(r => r.CreatedAt).HasColumnType("timestamptz").IsRequired();
        builder.Property(r => r.ResolvedAt).HasColumnType("timestamptz");

        builder.HasIndex(r => r.CycleCountResultId).IsUnique();
        builder.HasIndex(r => new { r.WarehouseId, r.ReconciliationStatus });
        builder.HasIndex(r => new { r.WarehouseId, r.LocationId, r.SkuId });
        builder.HasIndex(r => r.CreatedAt);
    }
}

public sealed class InventoryAdjustmentConfiguration : IEntityTypeConfiguration<Domain.Accuracy.Reconciliation.InventoryAdjustment>
{
    public void Configure(EntityTypeBuilder<Domain.Accuracy.Reconciliation.InventoryAdjustment> builder)
    {
        builder.ToTable("inventory_adjustment", table =>
            table.HasCheckConstraint("ck_inventory_adjustment_delta_nonzero", "quantity_delta <> 0"));

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Status)
            .HasConversion(
                value => value.ToString().ToUpperInvariant(),
                value => Enum.Parse<InventoryStatus>(value, ignoreCase: true))
            .HasMaxLength(16)
            .IsRequired();
        builder.Property(a => a.Reason)
            .HasConversion(
                value => value.ToString().ToUpperInvariant(),
                value => Enum.Parse<Domain.Accuracy.Reconciliation.AdjustmentReason>(value, ignoreCase: true))
            .HasMaxLength(24)
            .IsRequired();
        builder.Property(a => a.QuantityDelta).IsRequired();
        builder.Property(a => a.ResolvedBy).HasMaxLength(100);
        builder.Property(a => a.ResolutionNote).HasMaxLength(1000).IsRequired();
        builder.Property(a => a.ResolvedAt).HasColumnType("timestamptz").IsRequired();

        builder.HasIndex(a => a.ReconciliationId).IsUnique();
        builder.HasIndex(a => a.RequestId).IsUnique();
        builder.HasIndex(a => new { a.WarehouseId, a.SkuId });
    }
}

public sealed class InventoryAccuracySignalConfiguration : IEntityTypeConfiguration<Domain.Accuracy.InventoryAccuracySignal>
{
    public void Configure(EntityTypeBuilder<Domain.Accuracy.InventoryAccuracySignal> builder)
    {
        builder.ToTable("inventory_accuracy_signal", table =>
        {
            table.HasCheckConstraint("ck_inventory_accuracy_signal_system_quantity_non_negative", "system_quantity_at_signal >= 0");
            table.HasCheckConstraint("ck_inventory_accuracy_signal_allocated_non_negative", "allocated_at_signal >= 0");
            table.HasCheckConstraint("ck_inventory_accuracy_signal_available_non_negative", "available_at_signal >= 0");
        });

        builder.HasKey(s => s.Id);

        builder.Property(s => s.SignalType)
            .HasConversion(
                value => value.ToString().ToUpperInvariant(),
                value => Enum.Parse<AccuracySignalType>(value, ignoreCase: true))
            .HasMaxLength(24)
            .IsRequired();
        builder.Property(s => s.SourceType)
            .HasConversion(
                value => value.ToString().ToUpperInvariant(),
                value => Enum.Parse<AccuracySourceType>(value, ignoreCase: true))
            .HasMaxLength(24)
            .IsRequired();
        builder.Property(s => s.StatusAtSignal)
            .HasConversion(
                value => value.ToString().ToUpperInvariant(),
                value => Enum.Parse<InventoryStatus>(value, ignoreCase: true))
            .HasMaxLength(16)
            .IsRequired();
        builder.Property(s => s.SystemQuantityAtSignal).IsRequired();
        builder.Property(s => s.AllocatedAtSignal).IsRequired();
        builder.Property(s => s.AvailableAtSignal).IsRequired();
        builder.Property(s => s.OccurredAt).HasColumnType("timestamptz").IsRequired();
        builder.Property(s => s.RecordedAt).HasColumnType("timestamptz").IsRequired();

        builder.HasIndex(s => s.RequestId).IsUnique();
        builder.HasIndex(s => new { s.SkuId, s.LocationId });
        builder.HasIndex(s => new { s.WarehouseId, s.SignalType });
        builder.HasIndex(s => s.OccurredAt);
    }
}

public sealed class InventoryOperationConfiguration : IEntityTypeConfiguration<InventoryOperation>
{
    public void Configure(EntityTypeBuilder<InventoryOperation> builder)
    {
        builder.ToTable("inventory_operation");

        builder.HasKey(o => o.RequestId);

        builder.Property(o => o.OperationType).HasMaxLength(64).IsRequired();
        builder.Property(o => o.CreatedAt).HasColumnType("timestamptz").IsRequired();
    }
}

public sealed class ScanMovementEvidenceConfiguration : IEntityTypeConfiguration<Domain.Accuracy.Scanning.ScanMovementEvidence>
{
    public void Configure(EntityTypeBuilder<Domain.Accuracy.Scanning.ScanMovementEvidence> builder)
    {
        builder.ToTable("scan_movement_evidence", table =>
            table.HasCheckConstraint("ck_scan_movement_evidence_quantity_positive", "quantity > 0"));

        builder.HasKey(e => e.Id);

        builder.Property(e => e.SourceScanValue).HasMaxLength(128).IsRequired();
        builder.Property(e => e.SkuScanValue).HasMaxLength(128).IsRequired();
        builder.Property(e => e.DestinationScanValue).HasMaxLength(128).IsRequired();
        builder.Property(e => e.Quantity).IsRequired();
        builder.Property(e => e.DeviceId).HasMaxLength(64).IsRequired();
        builder.Property(e => e.OperatorId).HasMaxLength(64).IsRequired();
        builder.Property(e => e.OccurredAt).HasColumnType("timestamptz").IsRequired();

        builder.HasIndex(e => e.MovementId).IsUnique();
        builder.HasIndex(e => e.RequestId);
        builder.HasIndex(e => new { e.WarehouseId, e.SkuId });
        builder.HasIndex(e => e.OccurredAt);
    }
}
