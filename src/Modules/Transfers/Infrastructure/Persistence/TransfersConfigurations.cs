using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Modules.Transfers.Domain;

namespace Wms.Modules.Transfers.Infrastructure.Persistence;

public sealed class TransferOrderConfiguration : IEntityTypeConfiguration<TransferOrder>
{
    public void Configure(EntityTypeBuilder<TransferOrder> builder)
    {
        builder.ToTable("transfer_order", table =>
            table.HasCheckConstraint("ck_transfer_order_distinct_warehouses", "source_warehouse_id <> destination_warehouse_id"));

        builder.HasKey(t => t.Id);

        builder.Property(t => t.TransferNumber).HasMaxLength(64).IsRequired();
        builder.Property(t => t.Status)
            .HasConversion(
                value => value.ToString().ToUpperInvariant(),
                value => Enum.Parse<TransferStatus>(value, ignoreCase: true))
            .HasMaxLength(24)
            .IsRequired();
        builder.Property(t => t.ExternalReference).HasMaxLength(128);
        builder.Property(t => t.CreatedAt).HasColumnType("timestamptz").IsRequired();
        builder.Property(t => t.UpdatedAt).HasColumnType("timestamptz").IsRequired();
        builder.Property(t => t.ShippedAt).HasColumnType("timestamptz");
        builder.Property(t => t.CompletedAt).HasColumnType("timestamptz");
        builder.Property(t => t.CancelledAt).HasColumnType("timestamptz");

        builder.HasIndex(t => t.RequestId).IsUnique();
        builder.HasIndex(t => t.TransferNumber).IsUnique();
        builder.HasIndex(t => new { t.SourceWarehouseId, t.Status });
        builder.HasIndex(t => new { t.DestinationWarehouseId, t.Status });

        builder.HasMany(t => t.Lines)
            .WithOne()
            .HasForeignKey(l => l.TransferOrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class TransferLineConfiguration : IEntityTypeConfiguration<TransferLine>
{
    public void Configure(EntityTypeBuilder<TransferLine> builder)
    {
        builder.ToTable("transfer_line", table =>
        {
            table.HasCheckConstraint("ck_transfer_line_requested_positive", "requested_quantity > 0");
            table.HasCheckConstraint("ck_transfer_line_shipped_non_negative", "shipped_quantity >= 0");
            table.HasCheckConstraint("ck_transfer_line_received_non_negative", "received_quantity >= 0");
            table.HasCheckConstraint("ck_transfer_line_variance_non_negative", "confirmed_variance_quantity >= 0");
            table.HasCheckConstraint(
                "ck_transfer_line_no_negative_intransit",
                "received_quantity + confirmed_variance_quantity <= shipped_quantity");
        });

        builder.HasKey(l => l.Id);

        builder.Property(l => l.RequestedQuantity).IsRequired();
        builder.Property(l => l.ShippedQuantity).IsRequired();
        builder.Property(l => l.ReceivedQuantity).IsRequired();
        builder.Property(l => l.ConfirmedVarianceQuantity).IsRequired();
        builder.Property(l => l.CreatedAt).HasColumnType("timestamptz").IsRequired();
        builder.Property(l => l.UpdatedAt).HasColumnType("timestamptz").IsRequired();

        builder.HasIndex(l => new { l.TransferOrderId, l.SkuId }).IsUnique();
        builder.HasIndex(l => l.TransferOrderId);
    }
}

public sealed class TransferDiscrepancyConfiguration : IEntityTypeConfiguration<TransferDiscrepancy>
{
    public void Configure(EntityTypeBuilder<TransferDiscrepancy> builder)
    {
        builder.ToTable("transfer_discrepancy", table =>
            table.HasCheckConstraint("ck_transfer_discrepancy_quantity_positive", "quantity > 0"));

        builder.HasKey(d => d.Id);

        builder.Property(d => d.Quantity).IsRequired();
        builder.Property(d => d.Reason)
            .HasConversion(
                value => value.ToString().ToUpperInvariant(),
                value => Enum.Parse<TransferDiscrepancyReason>(value, ignoreCase: true))
            .HasMaxLength(24)
            .IsRequired();
        builder.Property(d => d.Note).HasMaxLength(1000);
        builder.Property(d => d.CreatedAt).HasColumnType("timestamptz").IsRequired();

        builder.HasIndex(d => d.RequestId).IsUnique();
        builder.HasIndex(d => d.TransferLineId);
    }
}

public sealed class TransferReceiveRecordConfiguration : IEntityTypeConfiguration<TransferReceiveRecord>
{
    public void Configure(EntityTypeBuilder<TransferReceiveRecord> builder)
    {
        builder.ToTable("transfer_receive_record", table =>
            table.HasCheckConstraint("ck_transfer_receive_record_quantity_positive", "quantity > 0"));

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Quantity).IsRequired();
        builder.Property(r => r.InventoryStatus).HasMaxLength(16).IsRequired();
        builder.Property(r => r.ReceivedAt).HasColumnType("timestamptz").IsRequired();

        builder.HasIndex(r => r.RequestId).IsUnique();
        builder.HasIndex(r => r.TransferLineId);
    }
}

public sealed class InboxMessageConfiguration : IEntityTypeConfiguration<Wms.Integration.Inbox.InboxMessage>
{
    public void Configure(EntityTypeBuilder<Wms.Integration.Inbox.InboxMessage> builder)
    {
        builder.ToTable("inbox_message");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.Consumer).HasMaxLength(64).IsRequired();
        builder.Property(m => m.ProcessedAt).HasColumnType("timestamptz").IsRequired();

        builder.HasIndex(m => new { m.Consumer, m.EventId }).IsUnique();
        builder.HasIndex(m => m.ProcessedAt);
    }
}
