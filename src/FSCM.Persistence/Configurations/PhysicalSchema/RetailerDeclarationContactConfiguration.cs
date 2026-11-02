using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class RetailerDeclarationContactConfiguration : IEntityTypeConfiguration<RetailerDeclarationContact>
{
    public void Configure(EntityTypeBuilder<RetailerDeclarationContact> builder)
    {
        builder.ToTable("RETAILER_DECLARATION_CONTACT");
        builder.HasKey(entity => entity.RetailerId);
        builder.Property(entity => entity.RetailerId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.ContactName).HasMaxLength(150).IsRequired();
        builder.Property(entity => entity.Phone).HasMaxLength(20).IsRequired();
        builder.Property(entity => entity.Email).HasMaxLength(150).IsRequired();
        builder.Property(entity => entity.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(entity => entity.UpdatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasIndex(entity => entity.Phone).IsUnique();
        builder.HasIndex(entity => entity.Email).IsUnique();
        builder.HasOne<Retailer>()
            .WithOne()
            .HasForeignKey<RetailerDeclarationContact>(entity => entity.RetailerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
