using FSCM.BuildingBlocks.Application;
using Microsoft.EntityFrameworkCore;

namespace FSCM.BuildingBlocks.Infrastructure.Persistence;

/// <summary>
/// Atomic counter: INSERT … ON CONFLICT DO UPDATE … RETURNING, executed as a plain command on the shared connection.
/// Safe under concurrent requests, so two Sales syncing at the same time never get the same DH-number.
/// </summary>
public sealed class PostgresSequenceGenerator(SharedDbContext db) : ISequenceGenerator
{
    public async Task<long> NextAsync(string key, CancellationToken ct = default)
    {
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            using var cmd = db.Database.GetDbConnection().CreateCommand();
            cmd.CommandText = "INSERT INTO shared.id_sequence(key, value) VALUES (@k, 1) ON CONFLICT (key) DO UPDATE SET value = shared.id_sequence.value + 1 RETURNING value";
            var p = cmd.CreateParameter(); p.ParameterName = "k"; p.Value = key; cmd.Parameters.Add(p);
            return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }
}

/// <summary>Holds cross-module technical tables (id_sequence, audit_log).</summary>
public sealed class SharedDbContext(DbContextOptions<SharedDbContext> options) : DbContext(options)
{
    public DbSet<IdSequence> Sequences => Set<IdSequence>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("shared");
        b.Entity<IdSequence>(e => { e.ToTable("id_sequence"); e.HasKey(x => x.Key); e.Property(x => x.Key).HasColumnName("key").HasMaxLength(60); e.Property(x => x.Value).HasColumnName("value"); });
        b.Entity<AuditLog>(e => { e.ToTable("audit_log"); e.HasKey(x => x.Id); });
    }
}

public sealed class IdSequence { public string Key { get; set; } = ""; public long Value { get; set; } }
public sealed class AuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = "";
    public string Action { get; set; } = "";
    public string Entity { get; set; } = "";
    public string EntityId { get; set; } = "";
    public string? Before { get; set; }
    public string? After { get; set; }
    public DateTime AtUtc { get; set; } = DateTime.UtcNow;
}
