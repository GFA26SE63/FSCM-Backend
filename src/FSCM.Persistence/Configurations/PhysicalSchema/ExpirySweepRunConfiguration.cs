using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class ExpirySweepRunConfiguration : IEntityTypeConfiguration<ExpirySweepRun>
{
    public void Configure(EntityTypeBuilder<ExpirySweepRun> builder)
    {
        builder.ToTable("EXPIRY_SWEEP_RUN");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.BusinessDate).HasColumnType("date").IsRequired();
        builder.Property(entity => entity.Status).HasMaxLength(30).IsRequired();
        builder.Property(entity => entity.Error).HasColumnType("text").IsRequired(false);
        builder.Property(entity => entity.StartedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(entity => entity.CompletedAt).HasColumnType("timestamp with time zone").IsRequired(false);
        builder.HasIndex(entity => entity.StartedAt);
    }
}
