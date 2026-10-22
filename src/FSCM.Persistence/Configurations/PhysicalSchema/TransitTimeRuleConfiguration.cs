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
        builder.Property(entity => entity.EstimatedTransportCost).HasPrecision(18, 2).IsRequired();
        builder.Property(entity => entity.MaxServingDistanceMeters).IsRequired();
        builder.Property(entity => entity.Version).IsRequired();
        builder.Property(entity => entity.EffectiveFrom).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(entity => entity.EffectiveTo).HasColumnType("timestamp with time zone").IsRequired(false);
        builder.Property(entity => entity.IsActive).IsRequired();
        builder.Property(entity => entity.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(entity => entity.UpdatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasIndex(entity => new
        {
            entity.WarehouseId,
            entity.MinDistanceMeters,
            entity.MaxDistanceMeters,
            entity.IsActive
        });
        builder.HasIndex(entity => new { entity.WarehouseId, entity.Version });
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_TRANSIT_TIME_RULE_EstimatedTransportCost", "\"EstimatedTransportCost\" >= 0"));
        builder.HasOne<Warehouse>()
            .WithMany()
            .HasForeignKey(entity => entity.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
