using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class RunPickingListConfiguration : IEntityTypeConfiguration<RunPickingList>
{
    public void Configure(EntityTypeBuilder<RunPickingList> builder)
    {
        builder.ToTable("RUN_PICKING_LIST");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.PickingCode).HasMaxLength(40).IsRequired();
        builder.Property(entity => entity.AllocationRunId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.AllocationPlanVersionId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.WarehouseId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.AssignedToUserId).HasColumnType("uuid").IsRequired(false);
        builder.Property(entity => entity.Status).HasMaxLength(30).IsRequired();
        builder.Property(entity => entity.IssuedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(entity => entity.StartedAt).HasColumnType("timestamp with time zone").IsRequired(false);
        builder.Property(entity => entity.CompletedAt).HasColumnType("timestamp with time zone").IsRequired(false);
        builder.Property(entity => entity.Version).IsRequired();
        builder.Property(entity => entity.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(entity => entity.UpdatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasIndex(entity => entity.PickingCode).IsUnique();

        // One list per warehouse per run: re-issuing finds this one instead of printing a second.
        builder.HasIndex(entity => new { entity.AllocationRunId, entity.WarehouseId }).IsUnique();
        builder.HasOne<AllocationRun>()
            .WithMany()
            .HasForeignKey(entity => entity.AllocationRunId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AllocationPlanVersion>()
            .WithMany()
            .HasForeignKey(entity => entity.AllocationPlanVersionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Warehouse>()
            .WithMany()
            .HasForeignKey(entity => entity.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
