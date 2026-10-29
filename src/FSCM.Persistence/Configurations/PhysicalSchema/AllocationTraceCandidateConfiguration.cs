using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class AllocationTraceCandidateConfiguration : IEntityTypeConfiguration<AllocationTraceCandidate>
{
    public void Configure(EntityTypeBuilder<AllocationTraceCandidate> builder)
    {
        builder.ToTable("ALLOCATION_TRACE_CANDIDATE");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.AllocationLineTraceId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.BatchId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.BatchCodeSnapshot).HasMaxLength(50).IsRequired();
        builder.Property(entity => entity.WarehouseId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.WarehouseCodeSnapshot).HasMaxLength(50).IsRequired();
        builder.Property(entity => entity.Stage).HasMaxLength(30).IsRequired();
        builder.Property(entity => entity.Passed).IsRequired();
        builder.Property(entity => entity.Reason).HasMaxLength(30).IsRequired();
        builder.Property(entity => entity.EstimatedArrivalAt).HasColumnType("timestamp with time zone").IsRequired(false);
        builder.Property(entity => entity.RemainingShelfLifeHours).HasPrecision(18, 3).IsRequired(false);
        builder.Property(entity => entity.RequiredShelfLifeHours).HasPrecision(18, 3).IsRequired(false);
        builder.Property(entity => entity.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasIndex(entity => new { entity.AllocationLineTraceId, entity.Stage, entity.BatchId }).IsUnique();
        builder.HasOne<AllocationLineTrace>()
            .WithMany()
            .HasForeignKey(entity => entity.AllocationLineTraceId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
