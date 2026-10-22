using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class AllocationRunOrderConfiguration : IEntityTypeConfiguration<AllocationRunOrder>
{
    public void Configure(EntityTypeBuilder<AllocationRunOrder> builder)
    {
        builder.ToTable("ALLOCATION_RUN_ORDER");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.AllocationRunId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.SalesOrderId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.OrderCodeSnapshot).HasMaxLength(40).IsRequired();
        builder.Property(entity => entity.RetailerId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.AcceptedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(entity => entity.DeliveryDeadline).HasColumnType("date").IsRequired(false);
        builder.Property(entity => entity.PriorityRank).IsRequired();
        builder.Property(entity => entity.Status).HasMaxLength(30).IsRequired();
        builder.Property(entity => entity.ShortageReason).HasMaxLength(1_000).IsRequired(false);
        builder.Property(entity => entity.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();

        // An order belongs to a run once, and holds one rank inside it.
        builder.HasIndex(entity => new { entity.AllocationRunId, entity.SalesOrderId }).IsUnique();
        builder.HasIndex(entity => new { entity.AllocationRunId, entity.PriorityRank }).IsUnique();
        builder.HasOne<AllocationRun>()
            .WithMany()
            .HasForeignKey(entity => entity.AllocationRunId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SalesOrder>()
            .WithMany()
            .HasForeignKey(entity => entity.SalesOrderId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
