using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class TransitTableConfiguration : IEntityTypeConfiguration<TransitTable>
{
    public void Configure(EntityTypeBuilder<TransitTable> builder)
    {
        builder.ToTable("TRANSIT_TABLE");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.Version).IsRequired();
        builder.Property(entity => entity.MaxServingDistanceMeters).IsRequired();
        builder.Property(entity => entity.EffectiveFrom).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(entity => entity.EffectiveTo).HasColumnType("timestamp with time zone").IsRequired(false);
        builder.Property(entity => entity.IsActive).IsRequired();
        builder.Property(entity => entity.Note).HasMaxLength(500).IsRequired(false);
        builder.Property(entity => entity.CreatedByUserId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(entity => entity.UpdatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasIndex(entity => entity.Version).IsUnique();

        // Only one version may be current, so a lookup never has to choose between two.
        builder.HasIndex(entity => entity.IsActive).IsUnique().HasFilter("\"IsActive\" = TRUE");

        builder.HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(entity => entity.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
