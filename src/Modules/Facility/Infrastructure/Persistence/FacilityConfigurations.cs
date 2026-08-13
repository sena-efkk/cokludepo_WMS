using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Modules.Facility.Domain;

namespace Wms.Modules.Facility.Infrastructure.Persistence;

public sealed class WarehouseConfiguration : IEntityTypeConfiguration<Warehouse>
{
    public void Configure(EntityTypeBuilder<Warehouse> builder)
    {
        builder.ToTable("warehouse");
        builder.HasKey(w => w.Id);

        builder.Property(w => w.Code).IsRequired().HasMaxLength(32);
        builder.Property(w => w.Name).IsRequired().HasMaxLength(150);
        builder.Property(w => w.AddressLine).HasMaxLength(300);
        builder.Property(w => w.City).HasMaxLength(100);
        builder.Property(w => w.CountryCode).HasMaxLength(2);
        builder.Property(w => w.Latitude).HasPrecision(9, 6);
        builder.Property(w => w.Longitude).HasPrecision(9, 6);
        builder.Property(w => w.IsActive).IsRequired();
        builder.Property(w => w.CreatedAt).HasColumnType("timestamptz").IsRequired();
        builder.Property(w => w.UpdatedAt).HasColumnType("timestamptz").IsRequired();

        builder.HasIndex(w => w.Code).IsUnique();
        builder.HasIndex(w => w.Name);
    }
}

public sealed class LocationConfiguration : IEntityTypeConfiguration<Location>
{
    public void Configure(EntityTypeBuilder<Location> builder)
    {
        builder.ToTable("location");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.Code).IsRequired().HasMaxLength(64);
        builder.Property(l => l.Name).IsRequired().HasMaxLength(150);
        builder.Property(l => l.Type).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(l => l.AllowsPicking).IsRequired();
        builder.Property(l => l.AllowsPutaway).IsRequired();
        builder.Property(l => l.AllowsReplenishment).IsRequired();
        builder.Property(l => l.HoldsInventory).IsRequired();
        builder.Property(l => l.IsActive).IsRequired();
        builder.Property(l => l.CreatedAt).HasColumnType("timestamptz").IsRequired();
        builder.Property(l => l.UpdatedAt).HasColumnType("timestamptz").IsRequired();

        builder.HasIndex(l => new { l.WarehouseId, l.Code }).IsUnique();
        builder.HasIndex(l => l.ParentLocationId);
        builder.HasIndex(l => l.WarehouseId);

        builder.HasOne<Warehouse>()
            .WithMany()
            .HasForeignKey(l => l.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Location>()
            .WithMany()
            .HasForeignKey(l => l.ParentLocationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
