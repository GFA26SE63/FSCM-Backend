using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using FSCM.Persistence.Configurations;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class SalesOrderQuoteConfiguration : IEntityTypeConfiguration<SalesOrderQuote>
{
    public void Configure(EntityTypeBuilder<SalesOrderQuote> builder)
    {
        builder.ToTable("SALES_ORDER_QUOTE");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Status).HasConversion(new EnumCodeConverter("QuoteStatus")).HasColumnType("smallint").IsRequired();
        builder.Property(x => x.TotalAmount).HasPrecision(18, 2).IsRequired();
        builder.Ignore(x => x.AllocationPlanVersionId);
        builder.HasIndex(x => new { x.SalesOrderId, x.RevisionNumber }).IsUnique();
        builder.HasIndex(x => new { x.AllocationRunId, x.Status });
        builder.ToTable(table => table.HasCheckConstraint("CK_SALES_ORDER_QUOTE_Status", "\"Status\" BETWEEN 0 AND 3"));
    }
}
