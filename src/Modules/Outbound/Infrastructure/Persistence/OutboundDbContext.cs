using Microsoft.EntityFrameworkCore;
using Wms.Modules.Outbound.Domain;

namespace Wms.Modules.Outbound.Infrastructure.Persistence;

public sealed class OutboundDbContext(DbContextOptions<OutboundDbContext> options) : DbContext(options)
{
    public DbSet<FulfillmentOrder> FulfillmentOrders => Set<FulfillmentOrder>();

    public DbSet<FulfillmentOrderLine> FulfillmentOrderLines => Set<FulfillmentOrderLine>();

    public DbSet<PickTask> PickTasks => Set<PickTask>();

    public DbSet<Package> Packages => Set<Package>();

    public DbSet<Shipment> Shipments => Set<Shipment>();

    public DbSet<Wms.Integration.Outbox.OutboxMessage> OutboxMessages => Set<Wms.Integration.Outbox.OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("outbound");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OutboundDbContext).Assembly);
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
