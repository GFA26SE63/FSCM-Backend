using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class RetailerConfiguration : IEntityTypeConfiguration<Retailer>
{
    public void Configure(EntityTypeBuilder<Retailer> builder)
    {
        builder.ToTable("RETAILER");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.RegionId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.UserAccountId).HasColumnType("uuid").IsRequired(false);
        builder.Property(entity => entity.Code).HasMaxLength(30).IsRequired();
        builder.Property(entity => entity.Name).HasMaxLength(200).IsRequired();
        builder.Property(entity => entity.ContactName).HasMaxLength(200).IsRequired(false);
        builder.Property(entity => entity.Phone).HasMaxLength(30).IsRequired(false);
        builder.Property(entity => entity.Email).HasMaxLength(150).IsRequired(false);
        builder.Property(entity => entity.Address).HasColumnType("text").IsRequired(false);
        builder.Property(entity => entity.Status).HasMaxLength(30).IsRequired();
        builder.Property(entity => entity.RetailerType).HasMaxLength(30).IsRequired();
        builder.Property(entity => entity.ForecastRevenue).HasPrecision(18, 2).IsRequired(false);
        builder.Property(entity => entity.StoreSizeSquareMeters).HasPrecision(12, 2).IsRequired(false);
        builder.Property(entity => entity.StoreSizeCategory).HasMaxLength(1).IsRequired();
        builder.Property(entity => entity.PurchasingCapacity).HasPrecision(18, 2).IsRequired(false);
        builder.Property(entity => entity.PurchaseFrequencyPerMonth).IsRequired();
        builder.Property(entity => entity.CreditTermDays).IsRequired();
        builder.Property(entity => entity.PotentialStatus).HasMaxLength(30).IsRequired(false);
        builder.Property(entity => entity.PotentialScore).HasPrecision(5, 2).IsRequired(false);
        builder.Property(entity => entity.AssessmentNote).HasColumnType("text").IsRequired(false);
        builder.Property(entity => entity.DeclaredByUserId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.ReviewedByUserId).HasColumnType("uuid").IsRequired(false);
        builder.Property(entity => entity.AssessedByUserId).HasColumnType("uuid").IsRequired(false);
        builder.Property(entity => entity.ReviewedAt).HasColumnType("timestamp with time zone").IsRequired(false);
        builder.Property(entity => entity.AssessedAt).HasColumnType("timestamp with time zone").IsRequired(false);
        builder.Property(entity => entity.ReviewReason).HasColumnType("text").IsRequired(false);
        builder.Property(entity => entity.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(entity => entity.UpdatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasIndex(entity => entity.Code).IsUnique();
        builder.HasIndex(entity => entity.UserAccountId).IsUnique();
        builder.HasOne<UserAccount>()
            .WithOne()
            .HasForeignKey<Retailer>(entity => entity.UserAccountId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("CK_RETAILER_CreditTermDays", "\"CreditTermDays\" IN (0, 7, 14, 30)");
            table.HasCheckConstraint("CK_RETAILER_PurchaseFrequencyPerMonth", "\"PurchaseFrequencyPerMonth\" > 0");
            table.HasCheckConstraint("CK_RETAILER_StoreSizeCategory", "\"StoreSizeCategory\" IN ('S', 'M', 'L')");
        });
        builder.HasOne<Region>()
            .WithMany()
            .HasForeignKey(entity => entity.RegionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(entity => entity.DeclaredByUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(entity => entity.ReviewedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(entity => entity.AssessedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
