using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.Identity;

public sealed class RetailerActivationChallengeConfiguration : IEntityTypeConfiguration<RetailerActivationChallenge>
{
    public void Configure(EntityTypeBuilder<RetailerActivationChallenge> builder)
    {
        builder.ToTable("RETAILER_ACTIVATION_CHALLENGE");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.OtpHash).HasMaxLength(128).IsRequired();
        builder.Property(item => item.SetupTokenHash).HasMaxLength(128);
        builder.Property(item => item.RequestedIpAddress).HasMaxLength(64);
        builder.HasIndex(item => item.SetupTokenHash).IsUnique();
        builder.HasIndex(item => new { item.UserAccountId, item.CreatedAt });
        builder.HasIndex(item => new { item.RequestedIpAddress, item.CreatedAt });
        builder.HasOne<UserAccount>().WithMany().HasForeignKey(item => item.UserAccountId).OnDelete(DeleteBehavior.Cascade);
    }
}
