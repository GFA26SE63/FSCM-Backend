using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class LoyaltyRewardConfiguration : IEntityTypeConfiguration<LoyaltyReward>
{
    public void Configure(EntityTypeBuilder<LoyaltyReward> builder)
    {
        builder.ToTable("LOYALTY_REWARD");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.LoyaltyProgramId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.Code).HasMaxLength(40).IsRequired();
        builder.Property(entity => entity.Name).HasMaxLength(200).IsRequired();
        builder.Property(entity => entity.Description).HasColumnType("text").IsRequired(false);
        builder.Property(entity => entity.RewardType).HasMaxLength(30).IsRequired();
        builder.Property(entity => entity.RequiredPoints).IsRequired();
        builder.Property(entity => entity.DiscountAmount).HasPrecision(18, 2).IsRequired(false);
        builder.Property(entity => entity.DiscountPercent).HasPrecision(5, 2).IsRequired(false);
        builder.Property(entity => entity.AvailableQuantity).IsRequired(false);
        builder.Property(entity => entity.IsActive).IsRequired();
        builder.Property(entity => entity.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(entity => entity.UpdatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasIndex(entity => entity.Code).IsUnique();
        builder.HasOne<LoyaltyProgram>()
            .WithMany()
            .HasForeignKey(entity => entity.LoyaltyProgramId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
