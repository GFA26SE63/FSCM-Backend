using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class SalesOrderAttachmentConfiguration : IEntityTypeConfiguration<SalesOrderAttachment>
{
    public void Configure(EntityTypeBuilder<SalesOrderAttachment> builder)
    {
        builder.ToTable("SALES_ORDER_ATTACHMENT");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.SalesOrderId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.UploadedByUserId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.AttachmentType).HasMaxLength(30).IsRequired();
        builder.Property(entity => entity.FileName).HasMaxLength(255).IsRequired();
        builder.Property(entity => entity.ContentType).HasMaxLength(100).IsRequired();
        builder.Property(entity => entity.BlobUrl).HasColumnType("text").IsRequired();
        builder.Property(entity => entity.UploadedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasOne<SalesOrder>()
            .WithMany()
            .HasForeignKey(entity => entity.SalesOrderId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(entity => entity.UploadedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
