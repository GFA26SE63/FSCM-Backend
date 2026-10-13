using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.Identity;

public sealed class UserCredentialConfiguration : IEntityTypeConfiguration<UserCredential>
{
    public void Configure(EntityTypeBuilder<UserCredential> builder)
    {
        builder.ToTable("USER_CREDENTIAL");
        builder.HasKey(entity => entity.Id);

        builder.Property(entity => entity.Id).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.UserAccountId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.PasswordHash).HasMaxLength(200).IsRequired();
        builder.Property(entity => entity.FailedLoginCount).IsRequired();
        builder.Property(entity => entity.LockoutEndAt).HasColumnType("timestamp with time zone").IsRequired(false);
        builder.Property(entity => entity.MustChangePassword).IsRequired();
        builder.Property(entity => entity.PasswordChangedAt).HasColumnType("timestamp with time zone").IsRequired(false);
        builder.Property(entity => entity.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(entity => entity.UpdatedAt).HasColumnType("timestamp with time zone").IsRequired();

        builder.HasIndex(entity => entity.UserAccountId).IsUnique();

        builder.HasOne<UserAccount>()
            .WithOne()
            .HasForeignKey<UserCredential>(entity => entity.UserAccountId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
