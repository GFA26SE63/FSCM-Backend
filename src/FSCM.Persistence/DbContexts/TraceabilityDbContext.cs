using FSCM.Domain.Entities;
using FSCM.Persistence.Configurations.PhysicalSchema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql;

namespace FSCM.Persistence.DbContexts;

/// <summary>Factory-owned database. External catalog, user and warehouse IDs are references, not cross-database FKs.</summary>
public sealed class TraceabilityDbContext(DbContextOptions<TraceabilityDbContext> options) : DbContext(options)
{
    public static void RequireSeparateDatabase(string warehouseConnection, string traceabilityConnection)
    {
        if (string.IsNullOrWhiteSpace(traceabilityConnection))
            throw new InvalidOperationException("Set ConnectionStrings:TraceabilityConnection to the separate factory database.");
        var warehouse = new NpgsqlConnectionStringBuilder(warehouseConnection);
        var factory = new NpgsqlConnectionStringBuilder(traceabilityConnection);
        if (string.IsNullOrWhiteSpace(factory.Database) || string.Equals(warehouse.Database, factory.Database, StringComparison.Ordinal))
            throw new InvalidOperationException("TraceabilityConnection must name a different database from DefaultConnection.");
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        builder.Properties<DateTimeOffset>().HaveConversion<UtcConverter>();
        builder.Properties<DateTimeOffset?>().HaveConversion<UtcConverter>();
    }
    private sealed class UtcConverter() : ValueConverter<DateTimeOffset, DateTimeOffset>(x => x.ToUniversalTime(), x => x);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Explicit list: never scan WMS configurations into the factory database.
        modelBuilder.ApplyConfiguration(new FactoryProductionBatchConfiguration(false));
        modelBuilder.ApplyConfiguration(new FactoryPalletConfiguration(false));
        modelBuilder.ApplyConfiguration(new FactoryCartonConfiguration());
        modelBuilder.ApplyConfiguration(new FactoryProductUnitConfiguration());
    }
}
