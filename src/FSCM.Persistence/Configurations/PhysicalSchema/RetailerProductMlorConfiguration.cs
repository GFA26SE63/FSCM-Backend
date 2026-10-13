using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class RetailerProductMlorConfiguration : IEntityTypeConfiguration<RetailerProductMlor>
{
    public void Configure(EntityTypeBuilder<RetailerProductMlor> builder)
    {
        builder.ToTable("RETAILER_PRODUCT_MLOR");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.RetailerId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.ProductId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.MinimumLifeRatio).HasPrecision(5, 4).IsRequired();
        builder.Property(entity => entity.MinimumLifeHours).IsRequired(false);
        builder.Property(entity => entity.EffectiveFrom).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(entity => entity.EffectiveTo).HasColumnType("timestamp with time zone").IsRequired(false);
        builder.Property(entity => entity.IsActive).IsRequired();
        builder.HasIndex(entity => new { entity.RetailerId, entity.ProductId })
            .IsUnique()
            .HasFilter("\"IsActive\" = TRUE");
        builder.HasOne<Retailer>()
            .WithMany()
            .HasForeignKey(entity => entity.RetailerId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(entity => entity.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
