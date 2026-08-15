using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Modules.Fulfillment.Domain;

namespace Wms.Modules.Fulfillment.Infrastructure.Persistence;

public sealed class SourcingRequestConfiguration : IEntityTypeConfiguration<SourcingRequest>
{
    public void Configure(EntityTypeBuilder<SourcingRequest> builder)
    {
        builder.ToTable("fulfillment_sourcing_request");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Destination).HasMaxLength(256).IsRequired();
        builder.Property(r => r.Status)
            .HasConversion(
                value => value.ToString().ToUpperInvariant(),
                value => Enum.Parse<SourcingStatus>(value, ignoreCase: true))
            .HasMaxLength(16)
            .IsRequired();
        builder.Property(r => r.CreatedAt).HasColumnType("timestamptz").IsRequired();
        builder.Property(r => r.UpdatedAt).HasColumnType("timestamptz").IsRequired();

        builder.HasIndex(r => r.RequestId).IsUnique();
        builder.HasIndex(r => r.Status);

        builder.HasMany(r => r.Lines)
            .WithOne()
            .HasForeignKey(l => l.SourcingRequestId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class SourcingLineConfiguration : IEntityTypeConfiguration<SourcingLine>
{
    public void Configure(EntityTypeBuilder<SourcingLine> builder)
    {
        builder.ToTable("fulfillment_sourcing_line", table =>
            table.HasCheckConstraint("ck_fulfillment_sourcing_line_quantity_positive", "quantity > 0"));

        builder.HasKey(l => l.Id);

        builder.Property(l => l.Quantity).IsRequired();
        builder.Property(l => l.CreatedAt).HasColumnType("timestamptz").IsRequired();
        builder.Property(l => l.UpdatedAt).HasColumnType("timestamptz").IsRequired();

        builder.HasIndex(l => new { l.SourcingRequestId, l.SkuId }).IsUnique();
        builder.HasIndex(l => l.SourcingRequestId);
    }
}

public sealed class SourcingDecisionConfiguration : IEntityTypeConfiguration<SourcingDecision>
{
    public void Configure(EntityTypeBuilder<SourcingDecision> builder)
    {
        builder.ToTable("fulfillment_sourcing_decision");

        builder.HasKey(d => d.Id);

        builder.Property(d => d.PlanSnapshot).HasColumnType("jsonb").IsRequired();
        builder.Property(d => d.CommittedAt).HasColumnType("timestamptz").IsRequired();
        builder.Property(d => d.CreatedAt).HasColumnType("timestamptz").IsRequired();
        builder.Property(d => d.UpdatedAt).HasColumnType("timestamptz").IsRequired();

        builder.HasIndex(d => d.RequestId).IsUnique();
        builder.HasIndex(d => d.SourcingRequestId).IsUnique();
    }
}

public sealed class SourcingOrderLinkConfiguration : IEntityTypeConfiguration<SourcingOrderLink>
{
    public void Configure(EntityTypeBuilder<SourcingOrderLink> builder)
    {
        builder.ToTable("fulfillment_sourcing_order_link");

        builder.HasKey(l => l.Id);

        builder.Property(l => l.OrderNumber).HasMaxLength(64).IsRequired();

        builder.HasIndex(l => l.DecisionId);
        builder.HasIndex(l => l.OutboundOrderId).IsUnique();
    }
}
