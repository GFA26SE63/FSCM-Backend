using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AUDIT_LOG");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.UserAccountId).HasColumnType("uuid").IsRequired(false);
        builder.Property(entity => entity.EntityName).HasMaxLength(100).IsRequired();
        builder.Property(entity => entity.EntityId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.Action).HasMaxLength(50).IsRequired();
        builder.Property(entity => entity.OldValues).HasColumnType("jsonb").IsRequired(false);
        builder.Property(entity => entity.NewValues).HasColumnType("jsonb").IsRequired(false);
        builder.Property(entity => entity.Reason).HasColumnType("text").IsRequired(false);
        builder.Property(entity => entity.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(entity => entity.UserAccountId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
