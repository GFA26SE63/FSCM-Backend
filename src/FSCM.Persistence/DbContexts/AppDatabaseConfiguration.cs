using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FSCM.Persistence.DbContexts;

/// <summary>The ignored API appsettings is the single database authority for runtime and EF tools.</summary>
public static class AppDatabaseConfiguration
{
    public static string? FindAppSettingsPath()
    {
        var candidates = new List<string>();
        for (var directory = new DirectoryInfo(Directory.GetCurrentDirectory()); directory is not null; directory = directory.Parent)
        {
            candidates.Add(Path.Combine(directory.FullName, "src", "FSCM.API", "appsettings.json"));
            candidates.Add(Path.Combine(directory.FullName, "FSCM-Backend", "src", "FSCM.API", "appsettings.json"));
        }
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "appsettings.json"));
        return candidates.FirstOrDefault(File.Exists);
    }

    public static string ReadConnectionString()
    {
        var path = FindAppSettingsPath()
            ?? throw new InvalidOperationException("The configured API appsettings.json was not found.");
        using var document = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions
        { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        var connection = document.RootElement.GetProperty("ConnectionStrings").GetProperty("DefaultConnection").GetString();
        return string.IsNullOrWhiteSpace(connection)
            ? throw new InvalidOperationException("DefaultConnection is missing from API appsettings.json.") : connection;
    }
}

public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(AppDatabaseConfiguration.ReadConnectionString()).Options);
}
