using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class UserAccountConfiguration : IEntityTypeConfiguration<UserAccount>
{
    public void Configure(EntityTypeBuilder<UserAccount> builder)
    {
        builder.ToTable("USER_ACCOUNT");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.EntraObjectId).HasMaxLength(150).IsRequired();
        builder.Property(entity => entity.Email).HasMaxLength(150).IsRequired();
        builder.Property(entity => entity.FullName).HasMaxLength(150).IsRequired();
        builder.Property(entity => entity.Role).HasMaxLength(30).IsRequired();
        builder.Property(entity => entity.IsActive).IsRequired();
        builder.Property(entity => entity.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(entity => entity.UpdatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasIndex(entity => entity.EntraObjectId).IsUnique();
        builder.HasIndex(entity => entity.Email).IsUnique();
    }
}

