using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class RetailerLoyaltyConfiguration : IEntityTypeConfiguration<RetailerLoyalty>
{
    public void Configure(EntityTypeBuilder<RetailerLoyalty> builder)
    {
        builder.ToTable("RETAILER_LOYALTY");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.RetailerId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.CurrentPoints).IsRequired();
        builder.Property(entity => entity.LifetimeEarnedPoints).IsRequired();
        builder.Property(entity => entity.LifetimeRedeemedPoints).IsRequired();
        builder.Property(entity => entity.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(entity => entity.UpdatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasOne<Retailer>()
            .WithOne()
            .HasForeignKey<RetailerLoyalty>(entity => entity.RetailerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
