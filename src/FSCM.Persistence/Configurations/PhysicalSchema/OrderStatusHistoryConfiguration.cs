using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class OrderStatusHistoryConfiguration : IEntityTypeConfiguration<OrderStatusHistory>
{
    public void Configure(EntityTypeBuilder<OrderStatusHistory> builder)
    {
        builder.ToTable("ORDER_STATUS_HISTORY");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.SalesOrderId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.ChangedByUserId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.FromStatus).HasMaxLength(30).IsRequired(false);
        builder.Property(entity => entity.ToStatus).HasMaxLength(30).IsRequired();
        builder.Property(entity => entity.Reason).HasColumnType("text").IsRequired(false);
        builder.Property(entity => entity.ChangedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasOne<SalesOrder>()
            .WithMany()
            .HasForeignKey(entity => entity.SalesOrderId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(entity => entity.ChangedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
