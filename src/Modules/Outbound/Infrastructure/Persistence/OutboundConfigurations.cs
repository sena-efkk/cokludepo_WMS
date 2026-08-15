using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Modules.Outbound.Domain;

namespace Wms.Modules.Outbound.Infrastructure.Persistence;

public sealed class FulfillmentOrderConfiguration : IEntityTypeConfiguration<FulfillmentOrder>
{
    public void Configure(EntityTypeBuilder<FulfillmentOrder> builder)
    {
        builder.ToTable("outbound_fulfillment_order");

        builder.HasKey(o => o.Id);

        builder.Property(o => o.OrderNumber).HasMaxLength(64).IsRequired();
        builder.Property(o => o.Status)
            .HasConversion(
                value => value.ToString().ToUpperInvariant(),
                value => Enum.Parse<OrderStatus>(value, ignoreCase: true))
            .HasMaxLength(24)
            .IsRequired();
        builder.Property(o => o.ExternalOrderReference).HasMaxLength(128);
        builder.Property(o => o.CreatedAt).HasColumnType("timestamptz").IsRequired();
        builder.Property(o => o.UpdatedAt).HasColumnType("timestamptz").IsRequired();
        builder.Property(o => o.AllocatedAt).HasColumnType("timestamptz");
        builder.Property(o => o.PickingStartedAt).HasColumnType("timestamptz");
        builder.Property(o => o.PackedAt).HasColumnType("timestamptz");
        builder.Property(o => o.ShippedAt).HasColumnType("timestamptz");
        builder.Property(o => o.CancelledAt).HasColumnType("timestamptz");

        builder.HasIndex(o => o.RequestId).IsUnique();
        builder.HasIndex(o => o.OrderNumber).IsUnique();
        builder.HasIndex(o => new { o.WarehouseId, o.Status });

        builder.HasMany(o => o.Lines)
            .WithOne()
            .HasForeignKey(l => l.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class FulfillmentOrderLineConfiguration : IEntityTypeConfiguration<FulfillmentOrderLine>
{
    public void Configure(EntityTypeBuilder<FulfillmentOrderLine> builder)
    {
        builder.ToTable("outbound_fulfillment_order_line", table =>
            table.HasCheckConstraint("ck_outbound_fulfillment_order_line_quantity_positive", "requested_quantity > 0"));

        builder.HasKey(l => l.Id);

        builder.Property(l => l.RequestedQuantity).IsRequired();
        builder.Property(l => l.CreatedAt).HasColumnType("timestamptz").IsRequired();
        builder.Property(l => l.UpdatedAt).HasColumnType("timestamptz").IsRequired();

        builder.HasIndex(l => new { l.OrderId, l.SkuId }).IsUnique();
        builder.HasIndex(l => l.OrderId);
    }
}

public sealed class PickTaskConfiguration : IEntityTypeConfiguration<PickTask>
{
    public void Configure(EntityTypeBuilder<PickTask> builder)
    {
        builder.ToTable("outbound_pick_task", table =>
        {
            table.HasCheckConstraint("ck_outbound_pick_task_required_positive", "required_quantity > 0");
            table.HasCheckConstraint("ck_outbound_pick_task_picked_non_negative", "picked_quantity >= 0");
            table.HasCheckConstraint("ck_outbound_pick_task_picked_not_exceeds_required", "picked_quantity <= required_quantity");
        });

        builder.HasKey(t => t.Id);

        builder.Property(t => t.RequiredQuantity).IsRequired();
        builder.Property(t => t.PickedQuantity).IsRequired();
        builder.Property(t => t.Status)
            .HasConversion(
                value => value.ToString().ToUpperInvariant(),
                value => Enum.Parse<PickTaskStatus>(value, ignoreCase: true))
            .HasMaxLength(16)
            .IsRequired();
        builder.Property(t => t.CreatedAt).HasColumnType("timestamptz").IsRequired();
        builder.Property(t => t.UpdatedAt).HasColumnType("timestamptz").IsRequired();
        builder.Property(t => t.StartedAt).HasColumnType("timestamptz");
        builder.Property(t => t.CompletedAt).HasColumnType("timestamptz");

        builder.HasIndex(t => t.ReservationLineId).IsUnique();
        builder.HasIndex(t => t.OrderId);
        builder.HasIndex(t => new { t.WarehouseId, t.Status });
        builder.HasIndex(t => t.CreatedAt);
    }
}

public sealed class PackageConfiguration : IEntityTypeConfiguration<Package>
{
    public void Configure(EntityTypeBuilder<Package> builder)
    {
        builder.ToTable("outbound_package");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.PackageNumber).HasMaxLength(64).IsRequired();
        builder.Property(p => p.Status)
            .HasConversion(
                value => value.ToString().ToUpperInvariant(),
                value => Enum.Parse<PackageStatus>(value, ignoreCase: true))
            .HasMaxLength(16)
            .IsRequired();
        builder.Property(p => p.CreatedAt).HasColumnType("timestamptz").IsRequired();
        builder.Property(p => p.PackedAt).HasColumnType("timestamptz").IsRequired();
        builder.Property(p => p.UpdatedAt).HasColumnType("timestamptz").IsRequired();

        builder.HasIndex(p => p.OrderId).IsUnique();
        builder.HasIndex(p => p.RequestId).IsUnique();
        builder.HasIndex(p => p.PackageNumber).IsUnique();
    }
}

public sealed class ShipmentConfiguration : IEntityTypeConfiguration<Shipment>
{
    public void Configure(EntityTypeBuilder<Shipment> builder)
    {
        builder.ToTable("outbound_shipment");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.ShipmentNumber).HasMaxLength(64).IsRequired();
        builder.Property(s => s.Status)
            .HasConversion(
                value => value.ToString().ToUpperInvariant(),
                value => Enum.Parse<ShipmentStatus>(value, ignoreCase: true))
            .HasMaxLength(16)
            .IsRequired();
        builder.Property(s => s.TrackingNumber).HasMaxLength(64);
        builder.Property(s => s.CarrierCode).HasMaxLength(16);
        builder.Property(s => s.CreatedAt).HasColumnType("timestamptz").IsRequired();
        builder.Property(s => s.ShippedAt).HasColumnType("timestamptz");
        builder.Property(s => s.UpdatedAt).HasColumnType("timestamptz").IsRequired();

        builder.HasIndex(s => s.OrderId).IsUnique();
        builder.HasIndex(s => s.RequestId).IsUnique();
        builder.HasIndex(s => s.ShipmentNumber).IsUnique();
    }
}

public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<Wms.Integration.Outbox.OutboxMessage>
{
    public void Configure(EntityTypeBuilder<Wms.Integration.Outbox.OutboxMessage> builder)
    {
        builder.ToTable("outbox_message");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.EventType).HasMaxLength(128).IsRequired();
        builder.Property(m => m.EventVersion).IsRequired();
        builder.Property(m => m.Payload).HasColumnType("jsonb").IsRequired();
        builder.Property(m => m.OccurredAt).HasColumnType("timestamptz").IsRequired();
        builder.Property(m => m.CreatedAt).HasColumnType("timestamptz").IsRequired();
        builder.Property(m => m.PublishedAt).HasColumnType("timestamptz");
        builder.Property(m => m.AttemptCount).IsRequired();
        builder.Property(m => m.LastError).HasMaxLength(2000);
        builder.Property(m => m.NextAttemptAt).HasColumnType("timestamptz");

        builder.HasIndex(m => m.EventId).IsUnique();
        builder.HasIndex(m => new { m.PublishedAt, m.NextAttemptAt });
        builder.HasIndex(m => m.CreatedAt);
    }
}
