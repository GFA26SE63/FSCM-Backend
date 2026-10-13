using FSCM.BuildingBlocks.Application;
using FSCM.Modules.Finance.Contracts;
using FSCM.Modules.Loyalty.Contracts;
using FSCM.Modules.Loyalty.Domain.Gifts;
using FSCM.Modules.Loyalty.Domain.Points;
using FSCM.Modules.Loyalty.Domain.Programs;
using FSCM.Modules.Ordering.Contracts;
using FSCM.SharedKernel.Domain;
using FSCM.SharedKernel.Ids;
using Microsoft.Extensions.DependencyInjection;

namespace FSCM.Modules.Loyalty.Application;

public sealed record ProgramInput(string Name, decimal AmountPerPoint, DateOnly ValidFrom, DateOnly ValidTo, int PointExpiryMonths, IReadOnlyList<string>? RetailerCodes, bool Active = true);
public sealed record ProgramDto(string Code, string Name, decimal AmountPerPoint, DateOnly ValidFrom, DateOnly ValidTo, int PointExpiryMonths, IReadOnlyList<string> RetailerCodes, bool Active);
public sealed record GiftInput(string Name, int PointCost, int Stock, string? ImageUrl, bool Active = true);
public sealed record GiftDto(string Code, string Name, int PointCost, int Stock, string? ImageUrl, bool Active);
public sealed record LedgerEntryDto(Guid Id, int Points, string Status, string Reference, string Note, DateTime CreatedAtUtc, DateOnly? ExpiresOn);
public sealed record RetailerPointsDto(LoyaltyBalanceDto Balance, string? ProgramCode, IReadOnlyList<LedgerEntryDto> Ledger);
public sealed record RedeemInput(string GiftCode, string? RetailerCode = null);
public sealed record RedemptionDto(string Code, string RetailerCode, string GiftCode, string GiftName, int Points, string Status, DateTime RequestedAtUtc, string? RejectReason);
public sealed record RejectRedemptionInput(string Reason);

public sealed class ProgramService(IProgramRepository repo, ILoyaltyUnitOfWork uow, IdGenerator ids)
{
    public async Task<IReadOnlyList<ProgramDto>> ListAsync(CancellationToken ct) => (await repo.AllAsync(ct)).Select(Map).ToList();
    public async Task<string> CreateAsync(ProgramInput x, CancellationToken ct)
    {
        var code = await ids.NextAsync("LP", n => IdFormat.LoyaltyProgram((int)n), ct);
        repo.Add(new LoyaltyProgram(code, x.Name, x.AmountPerPoint, x.ValidFrom, x.ValidTo, x.PointExpiryMonths, x.RetailerCodes ?? Array.Empty<string>()));
        await uow.SaveChangesAsync(ct); return code;
    }
    public async Task UpdateAsync(string code, ProgramInput x, CancellationToken ct)
    {
        var p = await repo.GetAsync(code, ct) ?? throw new BusinessRuleException("program.not_found", $"Không có chương trình {code}.");
        p.Update(x.Name, x.AmountPerPoint, x.ValidFrom, x.ValidTo, x.PointExpiryMonths, x.RetailerCodes ?? Array.Empty<string>(), x.Active); await uow.SaveChangesAsync(ct);
    }
    internal static ProgramDto Map(LoyaltyProgram p) => new(p.Id, p.Name, p.AmountPerPoint, p.ValidFrom, p.ValidTo, p.PointExpiryMonths, p.Retailers.Select(r => r.RetailerCode).ToList(), p.Active);
}

public sealed class GiftService(IGiftRepository repo, ILoyaltyUnitOfWork uow, IdGenerator ids)
{
    public async Task<IReadOnlyList<GiftDto>> ListAsync(bool activeOnly, CancellationToken ct) =>
        (await repo.AllAsync(ct)).Where(g => !activeOnly || g.Active).Select(g => new GiftDto(g.Id, g.Name, g.PointCost, g.Stock, g.ImageUrl, g.Active)).ToList();
    public async Task<string> CreateAsync(GiftInput x, CancellationToken ct)
    {
        var code = await ids.NextAsync("QUA", n => IdFormat.Gift((int)n), ct);
        repo.Add(new Gift(code, x.Name, x.PointCost, x.Stock, x.ImageUrl)); await uow.SaveChangesAsync(ct); return code;
    }
    public async Task UpdateAsync(string code, GiftInput x, CancellationToken ct)
    {
        var g = await repo.GetAsync(code, ct) ?? throw new BusinessRuleException("gift.not_found", $"Không có quà {code}.");
        g.Update(x.Name, x.PointCost, x.Stock, x.ImageUrl, x.Active); await uow.SaveChangesAsync(ct);
    }
}

/// <summary>Balance + ledger. A retailer may only read their own; staff read any.</summary>
public sealed class LoyaltyQueryService(IPointRepository points, IProgramRepository programs, ICurrentUser user, IClock clock)
{
    public async Task<RetailerPointsDto> GetAsync(string retailerCode, CancellationToken ct)
    {
        if (user.Role == Role.Retailer && user.RetailerCode != retailerCode) throw new UnauthorizedAccessException("Chỉ xem được điểm của chính mình.");
        var program = (await programs.AllAsync(ct)).FirstOrDefault(p => p.IsActiveOn(clock.Today) && p.Covers(retailerCode));
        var ledger = (await points.LedgerAsync(retailerCode, ct)).Select(e => new LedgerEntryDto(e.Id, e.Points, e.Status.ToString(), e.Reference, e.Note, e.CreatedAtUtc, e.ExpiresOn)).ToList();
        return new RetailerPointsDto(await LoyaltyModule.BalanceOf(points, retailerCode, ct), program?.Id, ledger);
    }
}

/// <summary>Workflow Requested → Approved → Delivered → Done / Rejected. Points are held when requested and refunded when rejected.</summary>
public sealed class RedemptionService(IGiftRepository gifts, IPointRepository points, ILoyaltyUnitOfWork uow, IdGenerator ids, ICurrentUser user, IClock clock)
{
    public async Task<IReadOnlyList<RedemptionDto>> ListAsync(string? status, CancellationToken ct)
    {
        RedemptionStatus? st = Enum.TryParse<RedemptionStatus>(status, true, out var s) ? s : null;
        return (await gifts.RedemptionsAsync(user.Role == Role.Retailer ? user.RetailerCode : null, st, ct)).Select(Map).ToList();
    }

    public async Task<string> RequestAsync(RedeemInput x, CancellationToken ct)
    {
        var retailer = user.Role == Role.Retailer ? user.RetailerCode! : x.RetailerCode ?? throw new BusinessRuleException("redemption.retailer", "Thiếu mã retailer.");
        var gift = await gifts.GetAsync(x.GiftCode, ct) ?? throw new BusinessRuleException("gift.not_found", $"Không có quà {x.GiftCode}.");
        var account = await points.GetAccountAsync(retailer, ct) ?? throw new BusinessRuleException("points.insufficient", "Retailer chưa có điểm.");
        account.Hold(gift.PointCost);                       // fails when not enough Available points (Pending points do not count)
        gift.TakeOne();
        var code = await ids.GiftRequestAsync(ct);
        gifts.Add(new GiftRedemption(code, retailer, gift.Id, gift.Name, gift.PointCost, clock.UtcNow));
        points.Add(new PointLedgerEntry(retailer, -gift.PointCost, LedgerStatus.Held, code, $"Giữ điểm đổi quà {gift.Name}", null, null, clock.UtcNow));
        uow.Enqueue(new GiftRedemptionRequested(code, retailer, gift.Id, gift.Name, gift.PointCost));
        await uow.SaveChangesAsync(ct); return code;
    }

    public async Task ApproveAsync(string code, CancellationToken ct)
    {
        var r = await Load(code, ct); r.Approve(user.FullName, clock.UtcNow);
        uow.Enqueue(new GiftRedemptionApproved(r.Id, r.RetailerCode, r.GiftName)); await uow.SaveChangesAsync(ct);
    }

    public async Task RejectAsync(string code, string reason, CancellationToken ct)
    {
        var r = await Load(code, ct); r.Reject(user.FullName, reason, clock.UtcNow);
        (await points.GetAccountAsync(r.RetailerCode, ct))!.Refund(r.Points);
        (await gifts.GetAsync(r.GiftCode, ct))?.ReturnOne();
        points.Add(new PointLedgerEntry(r.RetailerCode, r.Points, LedgerStatus.Refunded, r.Id, $"Hoàn điểm – yêu cầu {r.Id} bị từ chối", null, null, clock.UtcNow));
        uow.Enqueue(new GiftRedemptionRejected(r.Id, r.RetailerCode, r.GiftName, r.Points, reason));
        await uow.SaveChangesAsync(ct);
    }

    public async Task DeliverAsync(string code, CancellationToken ct)
    {
        var r = await Load(code, ct); r.Deliver(clock.UtcNow);
        uow.Enqueue(new GiftRedemptionDelivered(r.Id, r.RetailerCode, r.GiftName)); await uow.SaveChangesAsync(ct);
    }

    /// <summary>Gift handed over: the held points are spent for good.</summary>
    public async Task CompleteAsync(string code, CancellationToken ct)
    {
        var r = await Load(code, ct); r.Complete(clock.UtcNow);
        (await points.GetAccountAsync(r.RetailerCode, ct))!.Spend(r.Points);
        await uow.SaveChangesAsync(ct);
    }

    private async Task<GiftRedemption> Load(string code, CancellationToken ct) => await gifts.RedemptionAsync(code, ct) ?? throw new BusinessRuleException("redemption.not_found", $"Không có yêu cầu {code}.");
    private static RedemptionDto Map(GiftRedemption r) => new(r.Id, r.RetailerCode, r.GiftCode, r.GiftName, r.Points, r.Status.ToString(), r.RequestedAtUtc, r.RejectReason);
}

public sealed class LoyaltyModule(IPointRepository points) : ILoyaltyModule
{
    public Task<LoyaltyBalanceDto> BalanceAsync(string retailerCode, CancellationToken ct = default) => BalanceOf(points, retailerCode, ct);
    internal static async Task<LoyaltyBalanceDto> BalanceOf(IPointRepository points, string retailerCode, CancellationToken ct)
    {
        var a = await points.GetAccountAsync(retailerCode, ct);
        return new LoyaltyBalanceDto(retailerCode, a?.Available ?? 0, a?.Pending ?? 0, a?.Held ?? 0);
    }
}

/// <summary>Loyalty ← OrderDelivered: 1 point per 100,000đ after promotion. Credit orders are Pending until the receivable is paid.</summary>
public sealed class OrderDeliveredHandler(IProgramRepository programs, IPointRepository points, ILoyaltyUnitOfWork uow, IClock clock) : IIntegrationEventHandler<OrderDelivered>
{
    public async Task HandleAsync(OrderDelivered e, CancellationToken ct = default)
    {
        if (await points.EntryForOrderAsync(e.RetailerCode, e.OrderCode, ct) is not null) return;       // idempotent
        var program = (await programs.AllAsync(ct)).Where(p => p.IsActiveOn(e.DeliveredOn) && p.Covers(e.RetailerCode)).OrderByDescending(p => p.ValidFrom).FirstOrDefault();
        if (program is null) return;
        var pts = program.PointsFor(e.NetAmount);
        if (pts == 0) return;
        var account = await points.GetAccountAsync(e.RetailerCode, ct);
        if (account is null) { account = new PointAccount(e.RetailerCode); points.Add(account); }
        if (e.PaymentMethod == "Credit")
        {
            account.AddPending(pts);
            points.Add(new PointLedgerEntry(e.RetailerCode, pts, LedgerStatus.Pending, e.OrderCode, $"{e.OrderCode} · công nợ chưa thanh toán – cộng điểm khi đã thanh toán", program.Id, null, clock.UtcNow));
        }
        else
        {
            account.AddEarned(pts);
            points.Add(new PointLedgerEntry(e.RetailerCode, pts, LedgerStatus.Earned, e.OrderCode, $"{e.OrderCode} · đã giao, đã thanh toán", program.Id, program.ExpiryFor(e.DeliveredOn), clock.UtcNow));
        }
        await uow.SaveChangesAsync(ct);
    }
}

/// <summary>Loyalty ← CreditOrderPaid: Pending → Earned.</summary>
public sealed class CreditOrderPaidHandler(IProgramRepository programs, IPointRepository points, ILoyaltyUnitOfWork uow, IClock clock) : IIntegrationEventHandler<CreditOrderPaid>
{
    public async Task HandleAsync(CreditOrderPaid e, CancellationToken ct = default)
    {
        var entry = await points.EntryForOrderAsync(e.RetailerCode, e.OrderCode, ct);
        if (entry is null || entry.Status != LedgerStatus.Pending) return;                                // no program at the time, or already converted
        var program = entry.ProgramCode is null ? null : await programs.GetAsync(entry.ProgramCode, ct);
        (await points.GetAccountAsync(e.RetailerCode, ct))!.ConfirmPending(entry.Points);
        entry.Earn(program?.ExpiryFor(e.PaidOn), clock.UtcNow);
        await uow.SaveChangesAsync(ct);
    }
}

public static class LoyaltyApplication
{
    public static IServiceCollection AddLoyaltyApplication(this IServiceCollection s) => s
        .AddScoped<ILoyaltyModule, LoyaltyModule>()
        .AddScoped<ProgramService>().AddScoped<GiftService>().AddScoped<LoyaltyQueryService>().AddScoped<RedemptionService>()
        .AddScoped<IIntegrationEventHandler<OrderDelivered>, OrderDeliveredHandler>()
        .AddScoped<IIntegrationEventHandler<CreditOrderPaid>, CreditOrderPaidHandler>();
}
