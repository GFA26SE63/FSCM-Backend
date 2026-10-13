using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class RetailerSalesAssignmentConfiguration : IEntityTypeConfiguration<RetailerSalesAssignment>
{
    public void Configure(EntityTypeBuilder<RetailerSalesAssignment> builder)
    {
        builder.ToTable("RETAILER_SALES_ASSIGNMENT");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.RetailerId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.SalesGroupId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.AssignedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(entity => entity.UnassignedAt).HasColumnType("timestamp with time zone").IsRequired(false);
        builder.Property(entity => entity.IsActive).IsRequired();
        builder.HasOne<Retailer>()
            .WithMany()
            .HasForeignKey(entity => entity.RetailerId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SalesGroup>()
            .WithMany()
            .HasForeignKey(entity => entity.SalesGroupId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
