using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using FSCM.Persistence.Configurations;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class ReservationConfiguration : IEntityTypeConfiguration<Reservation>
{
    public void Configure(EntityTypeBuilder<Reservation> builder)
    {
        builder.ToTable("RESERVATION");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.AllocationId).HasColumnType("uuid").IsRequired(false);
        builder.Property(entity => entity.AllocationRunAllocationId).HasColumnType("uuid").IsRequired(false);
        builder.Property(entity => entity.BatchId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.Quantity).HasPrecision(18, 3).IsRequired();
        builder.Property(entity => entity.Status).HasConversion(new EnumCodeConverter("ReservationStatus")).HasColumnType("smallint").IsRequired();
        builder.Property(entity => entity.ReservedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(entity => entity.ReleasedAt).HasColumnType("timestamp with time zone").IsRequired(false);
        builder.Property(entity => entity.ConsumedAt).HasColumnType("timestamp with time zone").IsRequired(false);
        // One reservation per allocation on either path; the filters keep the two apart.
        builder.HasIndex(entity => entity.AllocationId)
            .IsUnique()
            .HasFilter("\"AllocationId\" IS NOT NULL");
        builder.HasIndex(entity => entity.AllocationRunAllocationId)
            .IsUnique()
            .HasFilter("\"AllocationRunAllocationId\" IS NOT NULL");
        builder.HasOne<Allocation>()
            .WithMany()
            .HasForeignKey(entity => entity.AllocationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AllocationRunAllocation>()
            .WithMany()
            .HasForeignKey(entity => entity.AllocationRunAllocationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Batch>()
            .WithMany()
            .HasForeignKey(entity => entity.BatchId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(table => table.HasCheckConstraint("CK_RESERVATION_Status", "\"Status\" BETWEEN 0 AND 3"));
    }
}
