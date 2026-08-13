using Microsoft.EntityFrameworkCore;
using Wms.Modules.Inventory.Domain;

namespace Wms.Modules.Inventory.Infrastructure.Persistence;

public sealed class InventoryDbContext(DbContextOptions<InventoryDbContext> options) : DbContext(options)
{
    public DbSet<InventoryBalance> InventoryBalances => Set<InventoryBalance>();

    public DbSet<InventoryReservation> InventoryReservations => Set<InventoryReservation>();

    public DbSet<ReservationLine> ReservationLines => Set<ReservationLine>();

    public DbSet<InventoryLedgerEntry> InventoryLedgerEntries => Set<InventoryLedgerEntry>();

    public DbSet<InventoryMovement> InventoryMovements => Set<InventoryMovement>();

    public DbSet<Domain.Accuracy.InventoryAccuracySignal> InventoryAccuracySignals => Set<Domain.Accuracy.InventoryAccuracySignal>();

    public DbSet<Domain.Accuracy.CycleCounting.CycleCountTask> CycleCountTasks => Set<Domain.Accuracy.CycleCounting.CycleCountTask>();

    public DbSet<Domain.Accuracy.CycleCounting.CycleCountResult> CycleCountResults => Set<Domain.Accuracy.CycleCounting.CycleCountResult>();

    public DbSet<Domain.Accuracy.Reconciliation.InventoryReconciliation> InventoryReconciliations => Set<Domain.Accuracy.Reconciliation.InventoryReconciliation>();

    public DbSet<Domain.Accuracy.Reconciliation.InventoryAdjustment> InventoryAdjustments => Set<Domain.Accuracy.Reconciliation.InventoryAdjustment>();

    public DbSet<Domain.Accuracy.Scanning.ScanMovementEvidence> ScanMovementEvidences => Set<Domain.Accuracy.Scanning.ScanMovementEvidence>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("inventory");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(InventoryDbContext).Assembly);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        TouchTimestamps();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override int SaveChanges()
    {
        TouchTimestamps();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        TouchTimestamps();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        TouchTimestamps();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void TouchTimestamps()
    {
        var now = DateTime.UtcNow;
        foreach (var entry in ChangeTracker.Entries<IHasTimestamps>())
        {
            if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
            }
        }
    }
}
