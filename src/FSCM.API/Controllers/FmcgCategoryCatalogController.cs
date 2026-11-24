using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;
using FSCM.Persistence.DbContexts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FSCM.API.Controllers;

public sealed record FmcgCategoryEntry(string Name, string Description);
public sealed record FmcgCategoryCatalogDocument(IReadOnlyCollection<FmcgCategoryEntry> Categories);
public sealed record FmcgCategoryEvaluationRequest(string Name, string? Description);
public sealed record FmcgCategoryEvaluationResult(bool IsFmcgRelated, IReadOnlyCollection<string> MatchedCategories, int CatalogVersion);
public sealed record FmcgCategoryCatalogVersionResult(int Version, DateTimeOffset UpdatedAt, Guid? UpdatedByUserId, bool IsActive);

[ApiController, Authorize]
[Route("api/v1/fmcg-category-catalog")]
public sealed class FmcgCategoryCatalogController(AppDbContext db, TimeProvider clock) : ControllerBase
{
    internal const string Key = "FmcgCategoryCatalogJson";

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var row = await ActiveQuery().FirstOrDefaultAsync(ct);
        return Ok(row is null ? new { version = 0, updatedAt = (DateTimeOffset?)null, document = new FmcgCategoryCatalogDocument([]) }
            : new { row.Version, row.UpdatedAt, document = Parse(row.ConfigValue) });
    }

    [HttpGet("history"), Authorize(Roles = SystemRoles.Admin)]
    public async Task<IReadOnlyCollection<FmcgCategoryCatalogVersionResult>> History(CancellationToken ct) =>
        await db.SystemConfigurationRecords.AsNoTracking().Where(x => x.ConfigKey == Key).OrderByDescending(x => x.Version)
            .Select(x => new FmcgCategoryCatalogVersionResult(x.Version, x.UpdatedAt, x.UpdatedByUserId, x.IsActive)).ToArrayAsync(ct);

    [HttpPut, Authorize(Roles = SystemRoles.Admin)]
    public async Task<IActionResult> Put([FromBody] JsonElement value, CancellationToken ct)
    {
        FmcgCategoryCatalogDocument document;
        try { document = Parse(value.GetRawText()); } catch (JsonException e) { return BadRequest(new { message = e.Message }); }
        var validation = Validate(document); if (validation is not null) return BadRequest(new { message = validation });
        var now = clock.GetUtcNow();
        var current = await db.SystemConfigurationRecords.Where(x => x.ConfigKey == Key && x.IsActive).SingleOrDefaultAsync(ct);
        var nextVersion = (await db.SystemConfigurationRecords.Where(x => x.ConfigKey == Key).MaxAsync(x => (int?)x.Version, ct) ?? 0) + 1;
        if (current is not null) { current.IsActive = false; current.UpdatedAt = now; }
        var actor = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : (Guid?)null;
        var row = new SystemConfiguration { Id = Guid.NewGuid(), ConfigKey = Key, ConfigValue = JsonSerializer.Serialize(document), Description = "Danh mục FMCG chuẩn do Admin doanh nghiệp quản lý", ValueType = "json", Version = nextVersion, IsActive = true, UpdatedByUserId = actor, EffectiveFrom = now, CreatedAt = now, UpdatedAt = now };
        db.SystemConfigurationRecords.Add(row); await db.SaveChangesAsync(ct);
        return Ok(new FmcgCategoryCatalogVersionResult(row.Version, row.UpdatedAt, row.UpdatedByUserId, true));
    }

    [HttpPost("evaluate")]
    public Task<FmcgCategoryEvaluationResult> Evaluate(FmcgCategoryEvaluationRequest request, CancellationToken ct) => EvaluateAsync(db, request, ct);

    internal static async Task<FmcgCategoryEvaluationResult> EvaluateAsync(AppDbContext db, FmcgCategoryEvaluationRequest request, CancellationToken ct)
    {
        var row = await db.SystemConfigurationRecords.AsNoTracking().Where(x => x.ConfigKey == Key && x.IsActive).OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct); if (row is null) return new(true, [], 0);
        var haystack = Normalize($"{request.Name} {request.Description}");
        var matches = Parse(row.ConfigValue).Categories.Where(x => IsMatch(haystack, x)).Select(x => x.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return new(matches.Length > 0, matches, row.Version);
    }

    internal static FmcgCategoryCatalogDocument Parse(string json)
    {
        var document = JsonSerializer.Deserialize<FmcgCategoryCatalogDocument>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new JsonException("JSON không hợp lệ.");
        return document.Categories is null ? throw new JsonException("JSON phải có mảng categories.") : document;
    }
    private static string? Validate(FmcgCategoryCatalogDocument d) => d.Categories.Count == 0 ? "Danh sách categories không được rỗng." : d.Categories.Any(x => x is null || string.IsNullOrWhiteSpace(x.Name) || string.IsNullOrWhiteSpace(x.Description)) ? "Mỗi category phải có name và description." : d.Categories.GroupBy(x => Normalize(x.Name)).Any(x => x.Count() > 1) ? "Tên category không được trùng nhau." : null;
    private static bool IsMatch(string haystack, FmcgCategoryEntry x) { if (haystack.Length == 0) return false; var name = Normalize(x.Name); return haystack.Contains(name) || name.Contains(haystack) || name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(t => t.Length >= 3 && haystack.Contains(t)) || Normalize(x.Description).Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(t => t.Length >= 5 && haystack.Contains(t)); }
    private static string Normalize(string? value) => new string((value ?? "").Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray()).Normalize(NormalizationForm.FormC).Trim().ToLowerInvariant();
    private IQueryable<SystemConfiguration> ActiveQuery() => db.SystemConfigurationRecords.AsNoTracking().Where(x => x.ConfigKey == Key && x.IsActive).OrderByDescending(x => x.Version);
}
