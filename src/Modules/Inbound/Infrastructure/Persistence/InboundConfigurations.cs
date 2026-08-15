using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Modules.Inbound.Domain;

namespace Wms.Modules.Inbound.Infrastructure.Persistence;

public sealed class InboundReceiptConfiguration : IEntityTypeConfiguration<InboundReceipt>
{
    public void Configure(EntityTypeBuilder<InboundReceipt> builder)
    {
        builder.ToTable("inbound_receipt");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.ReceiptNumber).HasMaxLength(64).IsRequired();
        builder.Property(r => r.Status)
            .HasConversion(
                value => value.ToString().ToUpperInvariant(),
                value => Enum.Parse<ReceiptStatus>(value, ignoreCase: true))
            .HasMaxLength(24)
            .IsRequired();
        builder.Property(r => r.ExternalReference).HasMaxLength(128);
        builder.Property(r => r.SourceType).HasMaxLength(32);
        builder.Property(r => r.CreatedAt).HasColumnType("timestamptz").IsRequired();
        builder.Property(r => r.UpdatedAt).HasColumnType("timestamptz").IsRequired();
        builder.Property(r => r.ReceivingStartedAt).HasColumnType("timestamptz");
        builder.Property(r => r.CompletedAt).HasColumnType("timestamptz");
        builder.Property(r => r.CancelledAt).HasColumnType("timestamptz");

        builder.HasIndex(r => r.RequestId).IsUnique();
        builder.HasIndex(r => r.ReceiptNumber).IsUnique();
        builder.HasIndex(r => new { r.WarehouseId, r.Status });

        builder.HasMany(r => r.Lines)
            .WithOne()
            .HasForeignKey(l => l.ReceiptId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class InboundReceiptLineConfiguration : IEntityTypeConfiguration<InboundReceiptLine>
{
    public void Configure(EntityTypeBuilder<InboundReceiptLine> builder)
    {
        builder.ToTable("inbound_receipt_line", table =>
        {
            table.HasCheckConstraint("ck_inbound_receipt_line_expected_non_negative", "expected_quantity >= 0");
            table.HasCheckConstraint("ck_inbound_receipt_line_received_non_negative", "received_quantity >= 0");
        });

        builder.HasKey(l => l.Id);

        builder.Property(l => l.ExpectedQuantity).IsRequired();
        builder.Property(l => l.ReceivedQuantity).IsRequired();
        builder.Property(l => l.Disposition)
            .HasConversion(
                value => value.HasValue ? value.Value.ToString().ToUpperInvariant() : null,
                value => string.IsNullOrEmpty(value) ? (ReceivingDisposition?)null : Enum.Parse<ReceivingDisposition>(value, ignoreCase: true))
            .HasMaxLength(16);
        builder.Property(l => l.CreatedAt).HasColumnType("timestamptz").IsRequired();
        builder.Property(l => l.UpdatedAt).HasColumnType("timestamptz").IsRequired();

        builder.HasIndex(l => new { l.ReceiptId, l.SkuId }).IsUnique();
        builder.HasIndex(l => l.ReceiptId);
    }
}

public sealed class ReceiptLineReceiveRecordConfiguration : IEntityTypeConfiguration<ReceiptLineReceiveRecord>
{
    public void Configure(EntityTypeBuilder<ReceiptLineReceiveRecord> builder)
    {
        builder.ToTable("inbound_receipt_record", table =>
            table.HasCheckConstraint("ck_inbound_receipt_record_quantity_positive", "quantity > 0"));

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Quantity).IsRequired();
        builder.Property(r => r.Disposition)
            .HasConversion(
                value => value.ToString().ToUpperInvariant(),
                value => Enum.Parse<ReceivingDisposition>(value, ignoreCase: true))
            .HasMaxLength(16)
            .IsRequired();
        builder.Property(r => r.InventoryStatus).HasMaxLength(16).IsRequired();
        builder.Property(r => r.ReceivedAt).HasColumnType("timestamptz").IsRequired();

        builder.HasIndex(r => r.RequestId).IsUnique();
        builder.HasIndex(r => r.ReceiptLineId);
        builder.HasIndex(r => r.InventoryOperationId);
    }
}

public sealed class PutawayTaskConfiguration : IEntityTypeConfiguration<PutawayTask>
{
    public void Configure(EntityTypeBuilder<PutawayTask> builder)
    {
        builder.ToTable("inbound_putaway_task", table =>
            table.HasCheckConstraint("ck_inbound_putaway_task_quantity_positive", "quantity > 0"));

        builder.HasKey(t => t.Id);

        builder.Property(t => t.InventoryStatus).HasMaxLength(16).IsRequired();
        builder.Property(t => t.Quantity).IsRequired();
        builder.Property(t => t.Status)
            .HasConversion(
                value => value.ToString().ToUpperInvariant(),
                value => Enum.Parse<PutawayTaskStatus>(value, ignoreCase: true))
            .HasMaxLength(16)
            .IsRequired();
        builder.Property(t => t.CreatedAt).HasColumnType("timestamptz").IsRequired();
        builder.Property(t => t.UpdatedAt).HasColumnType("timestamptz").IsRequired();
        builder.Property(t => t.StartedAt).HasColumnType("timestamptz");
        builder.Property(t => t.CompletedAt).HasColumnType("timestamptz");

        builder.HasIndex(t => t.ReceiveRecordId).IsUnique();
        builder.HasIndex(t => t.ReceiptId);
        builder.HasIndex(t => new { t.WarehouseId, t.Status });
        builder.HasIndex(t => t.CreatedAt);
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
