using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class TransitTimeRuleConfiguration : IEntityTypeConfiguration<TransitTimeRule>
{
    public void Configure(EntityTypeBuilder<TransitTimeRule> builder)
    {
        builder.ToTable("TRANSIT_TIME_RULE");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.WarehouseId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.MinDistanceMeters).IsRequired();
        builder.Property(entity => entity.MaxDistanceMeters).IsRequired();
        builder.Property(entity => entity.HandlingMinutes).IsRequired();
        builder.Property(entity => entity.TransitMinutes).IsRequired();
        builder.Property(entity => entity.MaxServingDistanceMeters).IsRequired();
        builder.Property(entity => entity.IsActive).IsRequired();
        builder.Property(entity => entity.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(entity => entity.UpdatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasOne<Warehouse>()
            .WithMany()
            .HasForeignKey(entity => entity.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
