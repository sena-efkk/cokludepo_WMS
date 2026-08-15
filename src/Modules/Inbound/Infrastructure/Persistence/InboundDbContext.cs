using Microsoft.EntityFrameworkCore;
using Wms.Modules.Inbound.Domain;

namespace Wms.Modules.Inbound.Infrastructure.Persistence;

public sealed class InboundDbContext(DbContextOptions<InboundDbContext> options) : DbContext(options)
{
    public DbSet<InboundReceipt> InboundReceipts => Set<InboundReceipt>();

    public DbSet<InboundReceiptLine> InboundReceiptLines => Set<InboundReceiptLine>();

    public DbSet<ReceiptLineReceiveRecord> ReceiptLineReceiveRecords => Set<ReceiptLineReceiveRecord>();

    public DbSet<PutawayTask> PutawayTasks => Set<PutawayTask>();

    public DbSet<Wms.Integration.Outbox.OutboxMessage> OutboxMessages => Set<Wms.Integration.Outbox.OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("inbound");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(InboundDbContext).Assembly);
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
