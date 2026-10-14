using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class SalesGroupMemberConfiguration : IEntityTypeConfiguration<SalesGroupMember>
{
    public void Configure(EntityTypeBuilder<SalesGroupMember> builder)
    {
        builder.ToTable("SALES_GROUP_MEMBER");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.SalesGroupId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.UserAccountId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.AssignedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(entity => entity.UnassignedAt).HasColumnType("timestamp with time zone").IsRequired(false);
        builder.Property(entity => entity.IsActive).IsRequired();
        builder.HasIndex(entity => entity.UserAccountId)
            .IsUnique()
            .HasFilter("\"IsActive\" = TRUE");
        builder.HasIndex(entity => new { entity.SalesGroupId, entity.AssignedAt });
        builder.HasOne<SalesGroup>()
            .WithMany()
            .HasForeignKey(entity => entity.SalesGroupId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(entity => entity.UserAccountId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
