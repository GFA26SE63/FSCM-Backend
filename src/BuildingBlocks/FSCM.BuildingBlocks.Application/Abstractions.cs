using FSCM.SharedKernel.Ids;

namespace FSCM.BuildingBlocks.Application;

public interface IClock { DateTime UtcNow { get; } DateOnly Today { get; } }

public sealed class SystemClock : IClock
{
    private static readonly TimeZoneInfo Vn = TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
    public DateTime UtcNow => DateTime.UtcNow;
    public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Vn));
}

public enum Role { Admin, Operator, SalesManager, WarehouseKeeper, Sales, Retailer }

public interface ICurrentUser
{
    string UserId { get; }
    string FullName { get; }
    Role Role { get; }
    string? WarehouseCode { get; }
    string? SalesGroupCode { get; }
    string? RetailerCode { get; }
}

public interface IUnitOfWork { Task<int> SaveChangesAsync(CancellationToken ct = default); }

/// <summary>Atomic counters stored in table id_sequence (key, value). Used with IdFormat.</summary>
public interface ISequenceGenerator { Task<long> NextAsync(string key, CancellationToken ct = default); }

public sealed class IdGenerator(ISequenceGenerator seq, IClock clock)
{
    public async Task<string> OrderAsync(CancellationToken ct = default) => IdFormat.Order(await seq.NextAsync("DH", ct));
    public async Task<string> LabelAsync(CancellationToken ct = default) => IdFormat.Label(await seq.NextAsync("TEM", ct));
    public async Task<string> DocumentAsync(DocumentType t, CancellationToken ct = default)
    {
        var today = clock.Today;
        return IdFormat.Document(t, today, (int)await seq.NextAsync(IdFormat.DocumentSequenceKey(t, today), ct));
    }
    public async Task<string> GiftRequestAsync(CancellationToken ct = default)
    {
        var today = clock.Today;
        return IdFormat.GiftRequest(today, (int)await seq.NextAsync($"DQ:{today:yyyyMMdd}", ct));
    }
    public async Task<string> NextAsync(string key, Func<long, string> format, CancellationToken ct = default) => format(await seq.NextAsync(key, ct));
}

public sealed record PageRequest(int Page = 1, int Size = 20, string? Search = null);
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int Size);
