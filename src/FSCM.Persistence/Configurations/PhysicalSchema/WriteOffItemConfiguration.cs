using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class WriteOffItemConfiguration : IEntityTypeConfiguration<WriteOffItem>
{
    public void Configure(EntityTypeBuilder<WriteOffItem> builder)
    {
        builder.ToTable("WRITE_OFF_ITEM");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.WriteOffId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.BatchId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.Quantity).HasPrecision(18, 3).IsRequired();
        builder.Property(entity => entity.Reason).HasColumnType("text").IsRequired(false);
        builder.Property(entity => entity.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasOne<WriteOff>()
            .WithMany()
            .HasForeignKey(entity => entity.WriteOffId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Batch>()
            .WithMany()
            .HasForeignKey(entity => entity.BatchId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
