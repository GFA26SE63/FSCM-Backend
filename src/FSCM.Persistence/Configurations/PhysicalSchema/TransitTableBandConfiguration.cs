using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class TransitTableBandConfiguration : IEntityTypeConfiguration<TransitTableBand>
{
    public void Configure(EntityTypeBuilder<TransitTableBand> builder)
    {
        builder.ToTable("TRANSIT_TABLE_BAND");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.TransitTableId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.MinDistanceMeters).IsRequired();
        builder.Property(entity => entity.MaxDistanceMeters).IsRequired();
        builder.Property(entity => entity.HandlingMinutes).IsRequired();
        builder.Property(entity => entity.TransitMinutes).IsRequired();
        builder.Property(entity => entity.EstimatedCostAmount).HasPrecision(18, 2).IsRequired();
        builder.Property(entity => entity.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasIndex(entity => new { entity.TransitTableId, entity.MinDistanceMeters }).IsUnique();
        builder.HasOne<TransitTable>()
            .WithMany()
            .HasForeignKey(entity => entity.TransitTableId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
