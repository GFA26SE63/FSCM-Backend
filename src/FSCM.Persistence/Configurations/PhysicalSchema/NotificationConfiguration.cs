using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("NOTIFICATION");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.UserAccountId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.OutboxMessageId).HasColumnType("uuid").IsRequired(false);
        builder.Property(entity => entity.SalesOrderId).HasColumnType("uuid").IsRequired(false);
        builder.Property(entity => entity.RelatedEntityType).HasMaxLength(50).IsRequired(false);
        builder.Property(entity => entity.RelatedEntityId).HasColumnType("uuid").IsRequired(false);
        builder.Property(entity => entity.DeepLink).HasMaxLength(500).IsRequired(false);
        builder.Property(entity => entity.Type).HasMaxLength(50).IsRequired();
        builder.Property(entity => entity.Title).HasMaxLength(200).IsRequired();
        builder.Property(entity => entity.Message).HasColumnType("text").IsRequired();
        builder.Property(entity => entity.IsRead).IsRequired();
        builder.Property(entity => entity.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(entity => entity.ReadAt).HasColumnType("timestamp with time zone").IsRequired(false);
        builder.HasIndex(entity => new { entity.UserAccountId, entity.OutboxMessageId })
            .IsUnique()
            .HasFilter("\"OutboxMessageId\" IS NOT NULL");
        builder.HasIndex(entity => new { entity.UserAccountId, entity.IsRead, entity.CreatedAt });
        builder.HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(entity => entity.UserAccountId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SalesOrder>()
            .WithMany()
            .HasForeignKey(entity => entity.SalesOrderId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<OutboxMessage>()
            .WithMany()
            .HasForeignKey(entity => entity.OutboxMessageId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
