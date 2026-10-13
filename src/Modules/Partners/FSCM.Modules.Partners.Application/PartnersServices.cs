using FSCM.BuildingBlocks.Application;
using FSCM.Modules.Partners.Contracts;
using FSCM.Modules.Partners.Domain.Areas;
using FSCM.Modules.Partners.Domain.Retailers;
using FSCM.SharedKernel.Domain;
using FSCM.SharedKernel.Ids;
using Microsoft.Extensions.DependencyInjection;

namespace FSCM.Modules.Partners.Application;

public sealed record AreaDto(string Code, string Name, bool Active);
public sealed record SalesGroupDto(string Code, string AreaCode, string Name, string? ManagerUserId, IReadOnlyList<SalesMemberDto> Members);
public sealed record SalesMemberDto(string UserId, string FullName);
public sealed record CreateGroupInput(string AreaCode, string Name, string? ManagerUserId);

/// <summary>Màn "Khu vực &amp; nhóm Sales" (Admin, Sales Manager).</summary>
public sealed class AreaService(IAreaRepository repo, IPartnersUnitOfWork uow)
{
    public async Task<IReadOnlyList<AreaDto>> AreasAsync(CancellationToken ct) => (await repo.AllAsync(ct)).Select(a => new AreaDto(a.Id, a.Name, a.Active)).ToList();

    public async Task<string> CreateAreaAsync(AreaDto x, CancellationToken ct)
    {
        Guard.Against(await repo.GetAsync(x.Code, ct) is not null, "area.exists", $"Khu vực {x.Code} đã tồn tại.");
        repo.Add(new Area(x.Code, x.Name)); await uow.SaveChangesAsync(ct); return x.Code;
    }

    public async Task UpdateAreaAsync(string code, AreaDto x, CancellationToken ct)
    {
        var a = await repo.GetAsync(code, ct) ?? throw new BusinessRuleException("area.not_found", $"Không có khu vực {code}.");
        a.Update(x.Name, x.Active); await uow.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<SalesGroupDto>> GroupsAsync(string? area, CancellationToken ct) => (await repo.GroupsAsync(area, ct)).Select(Map).ToList();

    public async Task<string> CreateGroupAsync(CreateGroupInput x, CancellationToken ct)
    {
        Guard.Against(await repo.GetAsync(x.AreaCode, ct) is null, "area.not_found", $"Không có khu vực {x.AreaCode}.");
        var existing = await repo.GroupsAsync(x.AreaCode, ct);
        var code = IdFormat.SalesGroup(x.AreaCode, existing.Count);
        repo.Add(new SalesGroup(code, x.AreaCode, x.Name, x.ManagerUserId)); await uow.SaveChangesAsync(ct); return code;
    }

    /// <summary>A Sales can only be in one group – adding them here moves them out of the old group.</summary>
    public async Task SetMembersAsync(string code, IReadOnlyList<SalesMemberDto> members, CancellationToken ct)
    {
        var g = await repo.GroupAsync(code, ct) ?? throw new BusinessRuleException("group.not_found", $"Không có nhóm {code}.");
        foreach (var m in members)
            if (await repo.GroupOfMemberAsync(m.UserId, ct) is { } other && other.Id != g.Id)
            {
                var removedFromOther = other.SetMembers(other.Members.Where(x => x.UserId != m.UserId).ToList());
                uow.Enqueue(new SalesGroupMembersChanged(other.Id, other.AreaCode, other.Members.Select(x => x.UserId).ToList(), removedFromOther));
            }
        var removed = g.SetMembers(members.Select(m => new SalesMember(m.UserId, m.FullName)));
        uow.Enqueue(new SalesGroupMembersChanged(g.Id, g.AreaCode, g.Members.Select(m => m.UserId).ToList(), removed));
        await uow.SaveChangesAsync(ct);
    }

    private static SalesGroupDto Map(SalesGroup g) => new(g.Id, g.AreaCode, g.Name, g.ManagerUserId, g.Members.Select(m => new SalesMemberDto(m.UserId, m.FullName)).ToList());
}

public sealed record RetailerInput(string? Code, string Name, string Phone, string Address, double Lat, double Lng, string AreaCode, string Kind, RetailerPotential Potential, int CreditTermDays);
public sealed record RetailerDetailDto(RetailerDto Retailer, RetailerPotential Potential, string Source, IReadOnlyList<MlorDto> Mlor);
public sealed record MlorDto(string Sku, decimal Ratio);

public sealed class RetailerService(IRetailerRepository repo, IAreaRepository areas, IPartnersUnitOfWork uow, IdGenerator ids, ICurrentUser user, IClock clock)
{
    /// <summary>Sales sees only retailers of their area (out-of-area blocked); staff sees all.</summary>
    public async Task<PagedResult<RetailerDto>> SearchAsync(PageRequest page, string? area, CancellationToken ct)
    {
        if (user.Role == Role.Sales)
            area = (await areas.GroupOfMemberAsync(user.UserId, ct))?.AreaCode ?? "__none__";
        return (await repo.SearchAsync(page.Search, area, null, ct)).Select(PartnersModule.Map).Page(page);
    }

    public async Task<RetailerDetailDto> GetAsync(string code, CancellationToken ct)
    {
        var r = await Load(code, ct);
        if (user.Role == Role.Sales)
            Guard.Against((await areas.GroupOfMemberAsync(user.UserId, ct))?.AreaCode != r.AreaCode, "retailer.out_of_area", "Điểm bán ngoài khu vực của bạn.");
        return new RetailerDetailDto(PartnersModule.Map(r), r.Potential, r.Source, r.Mlor.Select(m => new MlorDto(m.Sku, m.Ratio)).ToList());
    }

    /// <summary>Admin import / create. Code from SAP when given (8 digits), otherwise generated.</summary>
    public async Task<string> CreateAsync(RetailerInput x, CancellationToken ct)
    {
        Guard.Against(await areas.GetAsync(x.AreaCode, ct) is null, "area.not_found", $"Không có khu vực {x.AreaCode}.");
        var code = x.Code ?? await ids.NextAsync("RET", IdFormat.Retailer, ct);
        Guard.Against(await repo.GetAsync(code, ct) is not null, "retailer.exists", $"Mã {code} đã tồn tại.");
        var r = new Retailer(code, x.Name, x.Phone, x.Address, x.Lat, x.Lng, x.AreaCode, x.Kind, x.Potential, "Admin import", clock.UtcNow);
        r.SetCreditTerm(x.CreditTermDays, clock.UtcNow);
        repo.Add(r); await uow.SaveChangesAsync(ct); return code;
    }

    public async Task UpdateAsync(string code, RetailerInput x, CancellationToken ct)
    {
        var r = await Load(code, ct);
        r.UpdateProfile(x.Name, x.Phone, x.Address, x.Lat, x.Lng, x.AreaCode, x.Kind, x.Potential, clock.UtcNow);
        r.SetCreditTerm(x.CreditTermDays, clock.UtcNow);
        await uow.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<MlorDto>> MlorAsync(string code, CancellationToken ct) => (await Load(code, ct)).Mlor.Select(m => new MlorDto(m.Sku, m.Ratio)).ToList();

    public async Task SetMlorAsync(string code, IReadOnlyList<MlorDto> rows, CancellationToken ct)
    {
        var r = await Load(code, ct);
        foreach (var sku in r.Mlor.Select(m => m.Sku).Except(rows.Select(x => x.Sku)).ToList()) r.RemoveMlor(sku);
        foreach (var row in rows) r.SetMlor(row.Sku, row.Ratio, clock.UtcNow);
        await uow.SaveChangesAsync(ct);
    }

    private async Task<Retailer> Load(string code, CancellationToken ct) => await repo.GetAsync(code, ct) ?? throw new BusinessRuleException("retailer.not_found", $"Không có retailer {code}.");
}

public sealed record RegistrationInput(string Name, string Phone, string Address, double Lat, double Lng, string AreaCode, string Kind, RetailerPotential Potential);
public sealed record RegistrationDto(string Code, string Name, string Phone, string Address, string AreaCode, string Kind, RetailerPotential Potential,
    string DeclaredBy, string Status, string? RetailerCode, string? RejectReason, DateTime CreatedAtUtc);
public sealed record ApproveRegistrationInput(int CreditTermDays = 0);
public sealed record RejectInput(string Reason);

public sealed class RegistrationService(IRetailerRepository repo, IPartnersUnitOfWork uow, IdGenerator ids, ICurrentUser user, IClock clock)
{
    public async Task<IReadOnlyList<RegistrationDto>> ListAsync(string? status, CancellationToken ct)
    {
        RegistrationStatus? st = Enum.TryParse<RegistrationStatus>(status, true, out var s) ? s : null;
        return (await repo.RegistrationsAsync(st, user.Role == Role.Sales ? user.UserId : null, ct)).Select(Map).ToList();
    }

    /// <summary>Sales declares a new outlet with potential fields → KB-0000 waiting for Admin.</summary>
    public async Task<string> DeclareAsync(RegistrationInput x, CancellationToken ct)
    {
        var code = await ids.NextAsync("KB", n => IdFormat.RetailerRequest((int)n), ct);
        repo.Add(new RetailerRegistration(code, user.UserId, user.FullName, x.Name, x.Phone, x.Address, x.Lat, x.Lng, x.AreaCode, x.Kind, x.Potential, clock.UtcNow));
        await uow.SaveChangesAsync(ct); return code;
    }

    public async Task<string> ApproveAsync(string code, ApproveRegistrationInput x, CancellationToken ct)
    {
        var reg = await Load(code, ct);
        var retailerCode = await ids.NextAsync("RET", IdFormat.Retailer, ct);
        repo.Add(reg.Approve(retailerCode, user.FullName, x.CreditTermDays, clock.UtcNow));
        uow.Enqueue(new RetailerRegistered(reg.Id, retailerCode, reg.Name, reg.AreaCode, reg.DeclaredByUserId));
        await uow.SaveChangesAsync(ct); return retailerCode;
    }

    public async Task RejectAsync(string code, string reason, CancellationToken ct)
    {
        var reg = await Load(code, ct); reg.Reject(user.FullName, reason, clock.UtcNow);
        uow.Enqueue(new RetailerRegistrationRejected(reg.Id, reg.Name, reg.DeclaredByUserId, reason));
        await uow.SaveChangesAsync(ct);
    }

    private async Task<RetailerRegistration> Load(string code, CancellationToken ct) => await repo.RegistrationAsync(code, ct) ?? throw new BusinessRuleException("registration.not_found", $"Không có yêu cầu {code}.");
    private static RegistrationDto Map(RetailerRegistration r) => new(r.Id, r.Name, r.Phone, r.Address, r.AreaCode, r.Kind, r.Potential, r.DeclaredByName, r.Status.ToString(), r.RetailerCode, r.RejectReason, r.CreatedAtUtc);
}

public sealed class PartnersModule(IRetailerRepository retailers, IAreaRepository areas) : IPartnersModule
{
    public async Task<RetailerDto?> GetRetailerAsync(string code, CancellationToken ct = default) => await retailers.GetAsync(code, ct) is { } r ? Map(r) : null;
    public async Task<decimal?> MlorRatioAsync(string retailerCode, string sku, CancellationToken ct = default) => (await retailers.GetAsync(retailerCode, ct))?.MlorFor(sku);
    public async Task<SalesAssignmentDto?> AssignmentOfSalesAsync(string salesUserId, CancellationToken ct = default) =>
        await areas.GroupOfMemberAsync(salesUserId, ct) is { } g ? new SalesAssignmentDto(g.Id, g.AreaCode) : null;
    public async Task<bool> IsInSalesAreaAsync(string salesUserId, string retailerCode, CancellationToken ct = default)
    {
        var g = await areas.GroupOfMemberAsync(salesUserId, ct);
        var r = await retailers.GetAsync(retailerCode, ct);
        return g is not null && r is not null && r.AreaCode == g.AreaCode;
    }
    public async Task<IReadOnlyList<RetailerDto>> RetailersOfSalesAsync(string salesUserId, DateTime? changedSinceUtc, CancellationToken ct = default)
    {
        var g = await areas.GroupOfMemberAsync(salesUserId, ct);
        return g is null ? Array.Empty<RetailerDto>() : (await retailers.SearchAsync(null, g.AreaCode, changedSinceUtc, ct)).Select(Map).ToList();
    }
    internal static RetailerDto Map(Retailer r) => new(r.Id, r.Name, r.Phone, r.Address, r.Lat, r.Lng, r.AreaCode, r.Kind, r.CreditTermDays, r.Active, r.Potential.Class);
}

public static class PartnersApplication
{
    public static IServiceCollection AddPartnersApplication(this IServiceCollection s) => s
        .AddScoped<IPartnersModule, PartnersModule>()
        .AddScoped<AreaService>()
        .AddScoped<RetailerService>()
        .AddScoped<RegistrationService>();
}
