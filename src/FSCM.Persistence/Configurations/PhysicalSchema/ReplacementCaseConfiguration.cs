using FSCM.Domain.Entities;
using FSCM.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class ReplacementCaseConfiguration : IEntityTypeConfiguration<ReplacementCase>
{
    public void Configure(EntityTypeBuilder<ReplacementCase> builder)
    {
        builder.ToTable("REPLACEMENT_CASE", table => table.HasCheckConstraint("CK_REPLACEMENT_CASE_Status", "\"Status\" BETWEEN 0 AND 3"));
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnType("uuid");
        builder.Property(entity => entity.ReceiptDiscrepancyId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.WarehouseId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.Status).HasConversion(new EnumCodeConverter("ReplacementStatus")).HasColumnType("smallint").IsRequired();
        builder.Property(entity => entity.ApprovedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(entity => entity.ShippedAt).HasColumnType("timestamp with time zone");
        builder.Property(entity => entity.ReceivedAt).HasColumnType("timestamp with time zone");
        builder.Property(entity => entity.Version).IsConcurrencyToken().IsRequired();
        builder.Property(entity => entity.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(entity => entity.UpdatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasIndex(entity => entity.ReceiptDiscrepancyId).IsUnique();
        builder.HasOne<ReceiptDiscrepancy>().WithMany().HasForeignKey(entity => entity.ReceiptDiscrepancyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Warehouse>().WithMany().HasForeignKey(entity => entity.WarehouseId).OnDelete(DeleteBehavior.Restrict);
    }
}
