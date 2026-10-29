using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class SalesOrderQuoteConfiguration : IEntityTypeConfiguration<SalesOrderQuote>
{
    public void Configure(EntityTypeBuilder<SalesOrderQuote> builder)
    {
        builder.ToTable("SALES_ORDER_QUOTE");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Status).HasMaxLength(40).IsRequired();
        builder.Property(x => x.TotalAmount).HasPrecision(18, 2).IsRequired();
        builder.HasIndex(x => new { x.SalesOrderId, x.RevisionNumber }).IsUnique();
        builder.HasIndex(x => new { x.AllocationPlanVersionId, x.Status });
    }
}
