using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.Identity;

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("REFRESH_TOKEN");
        builder.HasKey(entity => entity.Id);

        builder.Property(entity => entity.Id).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.UserAccountId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.TokenHash).HasMaxLength(128).IsRequired();
        builder.Property(entity => entity.ExpiresAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(entity => entity.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(entity => entity.RevokedAt).HasColumnType("timestamp with time zone").IsRequired(false);
        builder.Property(entity => entity.ReplacedByTokenId).HasColumnType("uuid").IsRequired(false);
        builder.Property(entity => entity.CreatedByIpAddress).HasMaxLength(64).IsRequired(false);
        builder.Property(entity => entity.RevokedByIpAddress).HasMaxLength(64).IsRequired(false);

        builder.HasIndex(entity => entity.TokenHash).IsUnique();
        builder.HasIndex(entity => new { entity.UserAccountId, entity.ExpiresAt });

        builder.HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(entity => entity.UserAccountId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<RefreshToken>()
            .WithMany()
            .HasForeignKey(entity => entity.ReplacedByTokenId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
