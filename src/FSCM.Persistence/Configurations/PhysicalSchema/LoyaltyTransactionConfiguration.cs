using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class LoyaltyTransactionConfiguration : IEntityTypeConfiguration<LoyaltyTransaction>
{
    public void Configure(EntityTypeBuilder<LoyaltyTransaction> builder)
    {
        builder.ToTable("LOYALTY_TRANSACTION");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.LoyaltyProgramId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.RetailerLoyaltyId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.SalesOrderId).HasColumnType("uuid").IsRequired(false);
        builder.Property(entity => entity.CreatedByUserId).HasColumnType("uuid").IsRequired(false);
        builder.Property(entity => entity.TransactionType).HasMaxLength(30).IsRequired();
        builder.Property(entity => entity.Points).IsRequired();
        builder.Property(entity => entity.BalanceAfter).IsRequired();
        builder.Property(entity => entity.Note).HasColumnType("text").IsRequired(false);
        builder.Property(entity => entity.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasOne<LoyaltyProgram>()
            .WithMany()
            .HasForeignKey(entity => entity.LoyaltyProgramId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RetailerLoyalty>()
            .WithMany()
            .HasForeignKey(entity => entity.RetailerLoyaltyId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SalesOrder>()
            .WithMany()
            .HasForeignKey(entity => entity.SalesOrderId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(entity => entity.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

