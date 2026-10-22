using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class AllocationRunConfiguration : IEntityTypeConfiguration<AllocationRun>
{
    public void Configure(EntityTypeBuilder<AllocationRun> builder)
    {
        builder.ToTable("ALLOCATION_RUN");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.RunCode).HasMaxLength(40).IsRequired();
        builder.Property(entity => entity.BusinessDate).HasColumnType("date").IsRequired();
        builder.Property(entity => entity.CutoffAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(entity => entity.Status).HasMaxLength(30).IsRequired();
        builder.Property(entity => entity.AlgorithmSettings).HasColumnType("jsonb").IsRequired();
        builder.Property(entity => entity.TransitTableVersion).IsRequired();
        builder.Property(entity => entity.EtaScheduleVersion).IsRequired();
        builder.Property(entity => entity.OrderCount).IsRequired();
        builder.Property(entity => entity.FailureReason).HasMaxLength(1_000).IsRequired(false);
        builder.Property(entity => entity.StartedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(entity => entity.SnapshotCompletedAt).HasColumnType("timestamp with time zone").IsRequired(false);
        builder.Property(entity => entity.CompletedAt).HasColumnType("timestamp with time zone").IsRequired(false);
        builder.Property(entity => entity.Version).IsRequired();
        builder.Property(entity => entity.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(entity => entity.UpdatedAt).HasColumnType("timestamp with time zone").IsRequired();

        // One run per Vietnam business day. This is what stops a retry, a second scheduler instance
        // or a manual trigger from starting a competing run.
        builder.HasIndex(entity => entity.BusinessDate).IsUnique();
        builder.HasIndex(entity => entity.RunCode).IsUnique();
    }
}
