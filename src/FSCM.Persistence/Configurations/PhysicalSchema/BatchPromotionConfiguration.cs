using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class BatchPromotionConfiguration : IEntityTypeConfiguration<BatchPromotion>
{
    public void Configure(EntityTypeBuilder<BatchPromotion> builder)
    {
        builder.ToTable("BATCH_PROMOTION");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.BatchId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.PromotionId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.AssignedByUserId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.IsActive).IsRequired();
        builder.Property(entity => entity.AssignedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(entity => entity.RemovedAt).HasColumnType("timestamp with time zone").IsRequired(false);
        builder.HasIndex(entity => new { entity.BatchId, entity.PromotionId }).IsUnique();
        builder.HasOne<Batch>()
            .WithMany()
            .HasForeignKey(entity => entity.BatchId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Promotion>()
            .WithMany()
            .HasForeignKey(entity => entity.PromotionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(entity => entity.AssignedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
