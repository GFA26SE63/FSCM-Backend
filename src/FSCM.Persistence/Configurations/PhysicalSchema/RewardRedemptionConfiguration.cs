using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class RewardRedemptionConfiguration : IEntityTypeConfiguration<RewardRedemption>
{
    public void Configure(EntityTypeBuilder<RewardRedemption> builder)
    {
        builder.ToTable("REWARD_REDEMPTION");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.RetailerId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.LoyaltyRewardId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.LoyaltyTransactionId).HasColumnType("uuid").IsRequired(false);
        builder.Property(entity => entity.SalesOrderId).HasColumnType("uuid").IsRequired(false);
        builder.Property(entity => entity.RedeemedPoints).IsRequired();
        builder.Property(entity => entity.Status).HasMaxLength(30).IsRequired();
        builder.Property(entity => entity.RedeemedByUserId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.RedeemedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(entity => entity.FulfilledAt).HasColumnType("timestamp with time zone").IsRequired(false);
        builder.Property(entity => entity.CancelledAt).HasColumnType("timestamp with time zone").IsRequired(false);
        builder.Property(entity => entity.Note).HasColumnType("text").IsRequired(false);
        builder.Property(entity => entity.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(entity => entity.UpdatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasOne<Retailer>()
            .WithMany()
            .HasForeignKey(entity => entity.RetailerId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LoyaltyReward>()
            .WithMany()
            .HasForeignKey(entity => entity.LoyaltyRewardId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LoyaltyTransaction>()
            .WithOne()
            .HasForeignKey<RewardRedemption>(entity => entity.LoyaltyTransactionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SalesOrder>()
            .WithMany()
            .HasForeignKey(entity => entity.SalesOrderId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(entity => entity.RedeemedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
