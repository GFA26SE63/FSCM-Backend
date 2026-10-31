using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using FSCM.Persistence.Configurations;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class AllocationRunConfiguration : IEntityTypeConfiguration<AllocationRun>
{
    public void Configure(EntityTypeBuilder<AllocationRun> builder)
    {
        builder.ToTable("ALLOCATION_RUN");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.RunCode).HasMaxLength(40).IsRequired();
        builder.Property(entity => entity.SalesOrderId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.AttemptNumber).IsRequired();
        builder.Property(entity => entity.RetryOfAllocationRunId).HasColumnType("uuid").IsRequired(false);
        builder.Ignore(entity => entity.BusinessDate);
        builder.Ignore(entity => entity.CutoffAt);
        builder.Property(entity => entity.Status).HasConversion(new EnumCodeConverter("AllocationAttemptStatus")).HasColumnType("smallint").IsRequired();
        builder.Property(entity => entity.AlgorithmSettings).HasColumnType("jsonb").IsRequired();
        builder.Ignore(entity => entity.TransitTableVersion);
        builder.Ignore(entity => entity.EtaScheduleVersion);
        builder.Ignore(entity => entity.OrderCount);
        builder.Property(entity => entity.FailureReason).HasMaxLength(1_000).IsRequired(false);
        builder.Property(entity => entity.TechnicalErrorCode).HasMaxLength(100).IsRequired(false);
        builder.Property(entity => entity.StartedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(entity => entity.SnapshotCompletedAt).HasColumnType("timestamp with time zone").IsRequired(false);
        builder.Property(entity => entity.CompletedAt).HasColumnType("timestamp with time zone").IsRequired(false);
        builder.Property(entity => entity.Version).IsRequired();
        builder.Property(entity => entity.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(entity => entity.UpdatedAt).HasColumnType("timestamp with time zone").IsRequired();

        // One run per Vietnam business day. This is what stops a retry, a second scheduler instance
        // or a manual trigger from starting a competing run.
        builder.HasIndex(entity => new { entity.SalesOrderId, entity.AttemptNumber }).IsUnique();
        builder.HasIndex(entity => entity.RunCode).IsUnique();
        builder.HasOne<SalesOrder>().WithMany().HasForeignKey(entity => entity.SalesOrderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AllocationRun>().WithMany().HasForeignKey(entity => entity.RetryOfAllocationRunId).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(table => table.HasCheckConstraint("CK_ALLOCATION_RUN_Status", "\"Status\" BETWEEN 0 AND 4"));
    }
}
