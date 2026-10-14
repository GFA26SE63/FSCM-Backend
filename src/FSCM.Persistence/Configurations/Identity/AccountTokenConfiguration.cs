using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.Identity;

public sealed class AccountTokenConfiguration : IEntityTypeConfiguration<AccountToken>
{
    public void Configure(EntityTypeBuilder<AccountToken> builder)
    {
        builder.ToTable("ACCOUNT_TOKEN");
        builder.HasKey(entity => entity.Id);

        builder.Property(entity => entity.Id).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.UserAccountId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.Purpose).HasMaxLength(30).IsRequired();
        builder.Property(entity => entity.TokenHash).HasMaxLength(128).IsRequired();
        builder.Property(entity => entity.ExpiresAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(entity => entity.ConsumedAt).HasColumnType("timestamp with time zone").IsRequired(false);
        builder.Property(entity => entity.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();

        builder.HasIndex(entity => entity.TokenHash).IsUnique();
        builder.HasIndex(entity => new { entity.UserAccountId, entity.Purpose, entity.ExpiresAt });

        builder.HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(entity => entity.UserAccountId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
