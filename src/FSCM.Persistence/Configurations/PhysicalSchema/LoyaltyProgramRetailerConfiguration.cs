using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class LoyaltyProgramRetailerConfiguration : IEntityTypeConfiguration<LoyaltyProgramRetailer>
{
    public void Configure(EntityTypeBuilder<LoyaltyProgramRetailer> builder)
    {
        builder.ToTable("LOYALTY_PROGRAM_RETAILER");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.LoyaltyProgramId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.RetailerId).HasColumnType("uuid").IsRequired();
        builder.HasIndex(entity => new { entity.LoyaltyProgramId, entity.RetailerId }).IsUnique();
        builder.HasOne<LoyaltyProgram>().WithMany().HasForeignKey(entity => entity.LoyaltyProgramId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Retailer>().WithMany().HasForeignKey(entity => entity.RetailerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
