using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class SystemConfigurationConfiguration : IEntityTypeConfiguration<SystemConfiguration>
{
    public void Configure(EntityTypeBuilder<SystemConfiguration> builder)
    {
        builder.ToTable("SYSTEM_CONFIGURATION");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.ConfigKey).HasMaxLength(150).IsRequired();
        builder.Property(entity => entity.ConfigValue).HasColumnType("jsonb").IsRequired();
        builder.Property(entity => entity.Description).HasColumnType("text").IsRequired();
        builder.Property(entity => entity.ValueType).HasMaxLength(50).IsRequired();
        builder.Property(entity => entity.Version).IsRequired();
        builder.Property(entity => entity.IsActive).IsRequired();
        builder.Property(entity => entity.UpdatedByUserId).HasColumnType("uuid").IsRequired(false);
        builder.Property(entity => entity.EffectiveFrom).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(entity => entity.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(entity => entity.UpdatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasIndex(entity => new { entity.ConfigKey, entity.Version }).IsUnique();
        builder.HasIndex(entity => entity.ConfigKey)
            .IsUnique()
            .HasFilter("\"IsActive\" = TRUE");
        builder.HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(entity => entity.UpdatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
