using Microsoft.EntityFrameworkCore;
using Wms.Modules.MasterData.Domain;

namespace Wms.Modules.MasterData.Infrastructure.Persistence;

public sealed class MasterDataDbContext(DbContextOptions<MasterDataDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();

    public DbSet<Sku> Skus => Set<Sku>();

    public DbSet<SkuBarcode> SkuBarcodes => Set<SkuBarcode>();

    public DbSet<Uom> Uoms => Set<Uom>();

    public DbSet<Brand> Brands => Set<Brand>();

    public DbSet<Category> Categories => Set<Category>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("master_data");
        modelBuilder.HasSequence<long>("sku_code_seq", "master_data");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MasterDataDbContext).Assembly);

        modelBuilder.Entity<Uom>().HasData(
            new { Id = UomIds.Ea, Code = "EA", Name = "Each" },
            new { Id = UomIds.Box, Code = "BOX", Name = "Box" },
            new { Id = UomIds.Pcs, Code = "PCS", Name = "Piece" },
            new { Id = UomIds.Kg, Code = "KG", Name = "Kilogram" });
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
