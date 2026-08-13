using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Modules.MasterData.Domain;

namespace Wms.Modules.MasterData.Infrastructure.Persistence;

public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("product");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Name).IsRequired().HasMaxLength(200);
        builder.Property(p => p.Description).HasMaxLength(1000);
        builder.Property(p => p.IsActive).IsRequired();
        builder.Property(p => p.CreatedAt).HasColumnType("timestamptz").IsRequired();
        builder.Property(p => p.UpdatedAt).HasColumnType("timestamptz").IsRequired();

        builder.HasIndex(p => p.Name);
    }
}

public sealed class SkuConfiguration : IEntityTypeConfiguration<Sku>
{
    public void Configure(EntityTypeBuilder<Sku> builder)
    {
        builder.ToTable("sku", table => table.HasCheckConstraint("ck_sku_measurements_non_negative", "weight_kg >= 0 AND length_cm >= 0 AND width_cm >= 0 AND height_cm >= 0"));
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Code).IsRequired().HasMaxLength(64);
        builder.Property(s => s.Name).HasMaxLength(200);
        builder.Property(s => s.WeightKg).HasPrecision(12, 4);
        builder.Property(s => s.LengthCm).HasPrecision(12, 4);
        builder.Property(s => s.WidthCm).HasPrecision(12, 4);
        builder.Property(s => s.HeightCm).HasPrecision(12, 4);
        builder.Property(s => s.IsActive).IsRequired();
        builder.Property(s => s.CreatedAt).HasColumnType("timestamptz").IsRequired();
        builder.Property(s => s.UpdatedAt).HasColumnType("timestamptz").IsRequired();

        builder.HasIndex(s => s.Code).IsUnique();
        builder.HasIndex(s => s.ProductId);

        builder.HasOne(s => s.Product)
            .WithMany()
            .HasForeignKey(s => s.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.Uom)
            .WithMany()
            .HasForeignKey(s => s.UomId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(s => s.Barcodes)
            .WithOne()
            .HasForeignKey(b => b.SkuId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class SkuBarcodeConfiguration : IEntityTypeConfiguration<SkuBarcode>
{
    public void Configure(EntityTypeBuilder<SkuBarcode> builder)
    {
        builder.ToTable("sku_barcode");
        builder.HasKey(b => b.Id);

        builder.Property(b => b.Value).IsRequired().HasMaxLength(64);
        builder.Property(b => b.Type).HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.HasIndex(b => b.Value).IsUnique();
        builder.HasIndex(b => b.SkuId);
    }
}

public sealed class UomConfiguration : IEntityTypeConfiguration<Uom>
{
    public void Configure(EntityTypeBuilder<Uom> builder)
    {
        builder.ToTable("uom");
        builder.HasKey(u => u.Id);

        builder.Property(u => u.Code).IsRequired().HasMaxLength(16);
        builder.Property(u => u.Name).IsRequired().HasMaxLength(100);

        builder.HasIndex(u => u.Code).IsUnique();
    }
}

public sealed class BrandConfiguration : IEntityTypeConfiguration<Brand>
{
    public void Configure(EntityTypeBuilder<Brand> builder)
    {
        builder.ToTable("brand");
        builder.HasKey(b => b.Id);

        builder.Property(b => b.Name).IsRequired().HasMaxLength(150);

        builder.HasIndex(b => b.Name).IsUnique();
    }
}

public sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("category");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Name).IsRequired().HasMaxLength(150);

        builder.HasIndex(c => c.Name).IsUnique();
    }
}
