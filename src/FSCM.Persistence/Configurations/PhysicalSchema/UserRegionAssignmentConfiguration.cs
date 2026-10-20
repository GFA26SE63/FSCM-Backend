using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class UserRegionAssignmentConfiguration : IEntityTypeConfiguration<UserRegionAssignment>
{
    public void Configure(EntityTypeBuilder<UserRegionAssignment> builder)
    {
        builder.ToTable("USER_REGION_ASSIGNMENT");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.UserAccountId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.RegionId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.AssignedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(entity => entity.UnassignedAt).HasColumnType("timestamp with time zone").IsRequired(false);
        builder.Property(entity => entity.IsActive).IsRequired();
        builder.HasIndex(entity => new { entity.UserAccountId, entity.RegionId }).IsUnique();
        builder.HasIndex(entity => new { entity.RegionId, entity.IsActive });
        builder.HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(entity => entity.UserAccountId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Region>()
            .WithMany()
            .HasForeignKey(entity => entity.RegionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
