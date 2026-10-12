using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class WriteOffAttachmentConfiguration : IEntityTypeConfiguration<WriteOffAttachment>
{
    public void Configure(EntityTypeBuilder<WriteOffAttachment> builder)
    {
        builder.ToTable("WRITE_OFF_ATTACHMENT");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.WriteOffId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.UploadedByUserId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.FileName).HasMaxLength(255).IsRequired();
        builder.Property(entity => entity.ContentType).HasMaxLength(100).IsRequired();
        builder.Property(entity => entity.BlobUrl).HasColumnType("text").IsRequired();
        builder.Property(entity => entity.UploadedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasOne<WriteOff>()
            .WithMany()
            .HasForeignKey(entity => entity.WriteOffId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(entity => entity.UploadedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
