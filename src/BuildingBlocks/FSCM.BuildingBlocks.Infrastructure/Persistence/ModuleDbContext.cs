using System.Text.Json;
using FSCM.BuildingBlocks.Application;
using FSCM.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace FSCM.BuildingBlocks.Infrastructure.Persistence;

/// <summary>
/// Base DbContext for a module. Each module owns its own PostgreSQL schema in the same database
/// (one physical database, tables separated by schema). Integration events are written to the
/// module's outbox table in the same transaction (transactional outbox).
/// </summary>
public abstract class ModuleDbContext(DbContextOptions options) : DbContext(options), IUnitOfWork, IEventOutbox
{
    protected abstract string Schema { get; }
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();
    private readonly List<IIntegrationEvent> _pending = new();

    public void Enqueue(IIntegrationEvent e) => _pending.Add(e);

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Schema);
        b.Ignore<IDomainEvent>();            // domain events are in-memory only, never persisted
        b.Entity<OutboxMessage>(e =>
        {
            e.ToTable("outbox");
            e.HasKey(x => x.Id);
            e.Property(x => x.Type).HasMaxLength(300);
            e.HasIndex(x => x.ProcessedAtUtc);
        });
        b.ApplyConfigurationsFromAssembly(GetType().Assembly); // only this module's IEntityTypeConfiguration classes live in the assembly
    }

    public override async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        foreach (var entry in ChangeTracker.Entries())
        {
            // Optimistic locking: bump Version on every modified aggregate. EF compares the original value.
            if (entry.State == EntityState.Modified && entry.Entity.GetType().GetProperty("Version") is { } p && p.PropertyType == typeof(int))
                entry.Property("Version").CurrentValue = (int)entry.Property("Version").OriginalValue! + 1;
        }
        foreach (var e in _pending)
            Outbox.Add(new OutboxMessage { Id = e.EventId, Type = e.GetType().AssemblyQualifiedName!, Payload = JsonSerializer.Serialize(e, e.GetType()), OccurredAtUtc = e.OccurredAtUtc });
        _pending.Clear();
        try { return await base.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new BusinessRuleException("concurrency", "Dữ liệu đã được người khác cập nhật (version thay đổi). Tải lại và thử lại. " + ex.Entries.Count + " bản ghi xung đột.");
        }
    }
}

public sealed class OutboxMessage
{
    public Guid Id { get; set; }
    public string Type { get; set; } = "";
    public string Payload { get; set; } = "";
    public DateTime OccurredAtUtc { get; set; }
    public DateTime? ProcessedAtUtc { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
}
