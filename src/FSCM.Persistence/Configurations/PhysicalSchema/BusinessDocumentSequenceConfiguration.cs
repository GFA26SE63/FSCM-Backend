using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class BusinessDocumentSequenceConfiguration : IEntityTypeConfiguration<BusinessDocumentSequence>
{
    public void Configure(EntityTypeBuilder<BusinessDocumentSequence> builder)
    {
        builder.ToTable("BUSINESS_DOCUMENT_SEQUENCE");
        builder.HasKey(entity => new { entity.DocumentType, entity.BusinessDate });
        builder.Property(entity => entity.DocumentType).HasMaxLength(10);
        builder.Property(entity => entity.BusinessDate).HasColumnType("date");
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_BUSINESS_DOCUMENT_SEQUENCE_LastNumber", "\"LastNumber\" > 0"));
    }
}
