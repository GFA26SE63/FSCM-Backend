using FSCM.BuildingBlocks.Application;
using FSCM.Modules.Catalog.Contracts;
using FSCM.Modules.Inventory.Contracts;
using FSCM.Modules.Pricing.Contracts;
using FSCM.Modules.Pricing.Domain.BatchDiscounts;
using FSCM.Modules.Pricing.Domain.Promotions;
using FSCM.SharedKernel.Domain;
using FSCM.SharedKernel.Ids;
using Microsoft.Extensions.DependencyInjection;

namespace FSCM.Modules.Pricing.Application;

public sealed record PromotionRuleInput(string Sku, int MinQuantity, decimal Percent);
public sealed record PromotionInput(string Name, string Type, DateOnly ValidFrom, DateOnly ValidTo, IReadOnlyList<string>? RetailerCodes,
    IReadOnlyList<PromotionRuleInput>? Rules, IReadOnlyList<string>? GroupSkus, string? CategoryCode, int GroupMinQuantity, decimal GroupPercent);
public sealed record PromotionDto(string Code, string Name, string Type, string Status, DateOnly ValidFrom, DateOnly ValidTo, IReadOnlyList<string> RetailerCodes,
    IReadOnlyList<PromotionRuleDto> Rules, IReadOnlyList<string> GroupSkus, string? CategoryCode, int GroupMinQuantity, decimal GroupPercent);

/// <summary>Màn "Khuyến mãi".</summary>
public sealed class PromotionService(IPromotionRepository repo, IPricingUnitOfWork uow, IdGenerator ids)
{
    public async Task<IReadOnlyList<PromotionDto>> ListAsync(CancellationToken ct) => (await repo.AllAsync(ct)).Select(Map).ToList();
    public async Task<PromotionDto> GetAsync(string code, CancellationToken ct) => Map(await Load(code, ct));

    public async Task<string> CreateAsync(PromotionInput x, CancellationToken ct)
    {
        var code = await ids.NextAsync("KM", n => IdFormat.Promotion((int)n), ct);
        repo.Add(new Promotion(code, ToDetails(x))); await uow.SaveChangesAsync(ct); return code;
    }
    public async Task UpdateAsync(string code, PromotionInput x, CancellationToken ct) { (await Load(code, ct)).Update(ToDetails(x)); await uow.SaveChangesAsync(ct); }
    public async Task PauseAsync(string code, CancellationToken ct) { (await Load(code, ct)).Pause(); await uow.SaveChangesAsync(ct); }
    public async Task ResumeAsync(string code, CancellationToken ct) { (await Load(code, ct)).Resume(); await uow.SaveChangesAsync(ct); }

    private async Task<Promotion> Load(string code, CancellationToken ct) => await repo.GetAsync(code, ct) ?? throw new BusinessRuleException("promotion.not_found", $"Không có khuyến mãi {code}.");
    private static PromotionDetails ToDetails(PromotionInput x)
    {
        Guard.Against(!Enum.TryParse<PromotionType>(x.Type, true, out var type), "promotion.type", "Loại khuyến mãi: PerSku hoặc PerGroup.");
        return new PromotionDetails(x.Name, type, x.ValidFrom, x.ValidTo, x.RetailerCodes ?? Array.Empty<string>(),
            (x.Rules ?? Array.Empty<PromotionRuleInput>()).Select(r => new PromotionRule(r.Sku, r.MinQuantity, r.Percent)).ToList(),
            x.GroupSkus ?? Array.Empty<string>(), x.CategoryCode, x.GroupMinQuantity, x.GroupPercent);
    }
    internal static PromotionDto Map(Promotion p) => new(p.Id, p.Name, p.Type.ToString(), p.Status.ToString(), p.ValidFrom, p.ValidTo,
        p.Retailers.Select(r => r.RetailerCode).ToList(), p.Rules.Select(r => new PromotionRuleDto(r.Sku, r.MinQuantity, r.Percent)).ToList(),
        p.GroupSkus.Select(s => s.Sku).ToList(), p.CategoryCode, p.GroupMinQuantity, p.GroupPercent);
}

public sealed record BatchDiscountDto(string Code, string BatchCode, string Sku, decimal Percent, DateOnly From, DateOnly To, string Status, string CreatedBy);
public sealed record ApplyDiscountInput(string BatchCode, decimal Percent, DateOnly From, DateOnly To);

/// <summary>Màn "Giảm giá theo HSD": scan inventory → suggestions → Admin applies KMB.</summary>
public sealed class BatchDiscountService(IBatchDiscountRepository repo, IPricingUnitOfWork uow, IdGenerator ids,
    IInventoryModule inventory, ICatalogModule catalog, ICurrentUser user, IClock clock)
{
    public async Task<IReadOnlyList<BatchDiscountDto>> ListAsync(CancellationToken ct) => (await repo.AllAsync(ct)).Select(Map).ToList();

    /// <summary>"Quét tồn kho": batches whose remaining shelf life falls in a tier and that have no active discount yet.</summary>
    public async Task<IReadOnlyList<DiscountSuggestion>> ScanAsync(CancellationToken ct)
    {
        var today = clock.Today;
        var already = (await repo.ActiveOnAsync(today, ct)).Select(d => d.BatchCode).ToHashSet();
        var batches = new List<BatchForDiscount>();
        foreach (var p in await catalog.PublishedProductsAsync(ct))
            foreach (var b in await inventory.AllocatableBatchesAsync(p.Sku, ct))
                if (!already.Contains(b.BatchCode)) batches.Add(new BatchForDiscount(b.BatchCode, p.Sku, b.Expiry, p.ShelfLifeDays, b.Available));
        return BatchDiscountPolicy.Suggest(today, batches);
    }

    public async Task<IReadOnlyList<string>> ApplyAsync(IReadOnlyList<ApplyDiscountInput> items, CancellationToken ct)
    {
        var codes = new List<string>();
        var today = clock.Today;
        var active = (await repo.ActiveOnAsync(today, ct)).Select(d => d.BatchCode).ToHashSet();
        foreach (var i in items)
        {
            Guard.Against(active.Contains(i.BatchCode), "batch_discount.exists", $"Batch {i.BatchCode} đã có giảm giá đang áp dụng.");
            var sku = i.BatchCode.Split('-') is { Length: >= 3 } parts ? parts[2] : "";
            var code = await ids.DocumentAsync(DocumentType.BatchDiscount, ct);
            repo.Add(new BatchDiscount(code, i.BatchCode, sku, i.Percent, i.From, i.To, user.FullName)); codes.Add(code);
        }
        await uow.SaveChangesAsync(ct); return codes;
    }

    public async Task EndAsync(string code, CancellationToken ct)
    {
        (await repo.GetAsync(code, ct) ?? throw new BusinessRuleException("batch_discount.not_found", $"Không có {code}.")).End();
        await uow.SaveChangesAsync(ct);
    }
    private static BatchDiscountDto Map(BatchDiscount d) => new(d.Id, d.BatchCode, d.Sku, d.Percent, d.From, d.To, d.Status.ToString(), d.CreatedBy);
}

public sealed class PricingModule(IPromotionRepository promotions, IBatchDiscountRepository batchDiscounts, ICatalogModule catalog) : IPricingModule
{
    public async Task<QuoteDto> QuoteAsync(string retailerCode, IReadOnlyList<QuoteLineInput> lines, DateOnly onDate, CancellationToken ct = default)
    {
        Guard.Against(lines.Count == 0 || lines.Any(l => l.Quantity <= 0), "quote.lines", "Đơn cần ít nhất một dòng với số lượng > 0.");
        var priceLines = new List<PriceLine>(); var categories = new Dictionary<string, string?>();
        foreach (var l in lines)
        {
            var p = await catalog.GetProductAsync(l.Sku, ct) ?? throw new BusinessRuleException("product.not_found", $"Không có sản phẩm {l.Sku}.");
            Guard.Against(p.Status != "Published", "product.not_published", $"{l.Sku} chưa publish hoặc đã ngừng kinh doanh.");
            categories[l.Sku] = p.CategoryCode; priceLines.Add(new PriceLine(l.Sku, l.Quantity, p.BasePrice));
        }
        var r = PromotionCalculator.Calculate(await promotions.ActiveOnAsync(onDate, ct), retailerCode, onDate, priceLines, sku => categories.GetValueOrDefault(sku));
        return new QuoteDto(
            r.Lines.Select(l => new QuoteLineDto(l.LineNo, l.Sku, l.Quantity, l.UnitPrice, l.Gross, l.PromotionCode, l.Percent, l.Discount, l.Net)).ToList(),
            r.Applied.Select(a => new AppliedPromotionDto(a.Code, a.Name, a.Amount, a.Detail)).ToList(), r.Gross, r.Discount, r.Net);
    }

    public async Task<IReadOnlyDictionary<string, decimal>> ActiveBatchDiscountsAsync(DateOnly onDate, CancellationToken ct = default) =>
        (await batchDiscounts.ActiveOnAsync(onDate, ct)).GroupBy(d => d.BatchCode).ToDictionary(g => g.Key, g => g.Max(x => x.Percent));

    public async Task<IReadOnlyList<PromotionSnapshotDto>> ActivePromotionsAsync(DateOnly onDate, CancellationToken ct = default) =>
        (await promotions.ActiveOnAsync(onDate, ct)).Select(p => new PromotionSnapshotDto(p.Id, p.Name, p.Type.ToString(), p.ValidFrom, p.ValidTo,
            p.Retailers.Select(r => r.RetailerCode).ToList(), p.Rules.Select(r => new PromotionRuleDto(r.Sku, r.MinQuantity, r.Percent)).ToList(),
            p.GroupSkus.Select(s => s.Sku).ToList(), p.CategoryCode, p.GroupMinQuantity, p.GroupPercent)).ToList();
}

public static class PricingApplication
{
    public static IServiceCollection AddPricingApplication(this IServiceCollection s) => s
        .AddScoped<IPricingModule, PricingModule>()
        .AddScoped<PromotionService>()
        .AddScoped<BatchDiscountService>();
}
