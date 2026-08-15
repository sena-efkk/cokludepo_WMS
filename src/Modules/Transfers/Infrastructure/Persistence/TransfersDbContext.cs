using Microsoft.EntityFrameworkCore;
using Wms.Modules.Transfers.Domain;

namespace Wms.Modules.Transfers.Infrastructure.Persistence;

public sealed class TransfersDbContext(DbContextOptions<TransfersDbContext> options) : DbContext(options)
{
    public DbSet<TransferOrder> TransferOrders => Set<TransferOrder>();

    public DbSet<TransferLine> TransferLines => Set<TransferLine>();

    public DbSet<TransferDiscrepancy> TransferDiscrepancies => Set<TransferDiscrepancy>();

    public DbSet<TransferReceiveRecord> TransferReceiveRecords => Set<TransferReceiveRecord>();

    public DbSet<Wms.Integration.Inbox.InboxMessage> InboxMessages => Set<Wms.Integration.Inbox.InboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("transfers");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TransfersDbContext).Assembly);
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
