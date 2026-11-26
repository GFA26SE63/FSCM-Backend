using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.Identity;

public sealed class ScreenApiMappingConfiguration : IEntityTypeConfiguration<ScreenApiMapping>
{
    public void Configure(EntityTypeBuilder<ScreenApiMapping> builder)
    {
        builder.ToTable("SYS_SCREEN_API_MAPPING");
        builder.HasKey(row => new { row.BusinessRole, row.ApiCode, row.ScreenCode });
        builder.Property(row => row.BusinessRole).HasMaxLength(30);
        builder.Property(row => row.ApiCode).HasMaxLength(100);
        builder.Property(row => row.ScreenCode).HasMaxLength(100);
        builder.HasIndex(row => new { row.BusinessRole, row.ScreenCode });
    }
}
