using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class UserWarehouseAssignmentConfiguration : IEntityTypeConfiguration<UserWarehouseAssignment>
{
    public void Configure(EntityTypeBuilder<UserWarehouseAssignment> builder)
    {
        builder.ToTable("USER_WAREHOUSE_ASSIGNMENT");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.UserAccountId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.WarehouseId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.AssignedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(entity => entity.IsActive).IsRequired();
        builder.HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(entity => entity.UserAccountId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Warehouse>()
            .WithMany()
            .HasForeignKey(entity => entity.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
