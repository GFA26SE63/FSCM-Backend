namespace FSCM.Modules.Pricing.Contracts;

public sealed record QuoteLineInput(string Sku, int Quantity);

public sealed record QuoteLineDto(int LineNo, string Sku, int Quantity, decimal UnitPrice, decimal GrossAmount,
    string? PromotionCode, decimal PromotionPercent, decimal PromotionAmount, decimal NetAmount);

public sealed record AppliedPromotionDto(string Code, string Name, decimal Amount, string Detail);

/// <summary>Price calculation shared by Ordering (authoritative) and the mobile app (offline estimate).</summary>
public sealed record QuoteDto(IReadOnlyList<QuoteLineDto> Lines, IReadOnlyList<AppliedPromotionDto> Applied, decimal Gross, decimal Discount, decimal Net);

public sealed record PromotionRuleDto(string Sku, int MinQuantity, decimal Percent);

/// <summary>Promotion as cached on the phone for the offline estimate.</summary>
public sealed record PromotionSnapshotDto(string Code, string Name, string Type, DateOnly ValidFrom, DateOnly ValidTo,
    IReadOnlyList<string> RetailerCodes, IReadOnlyList<PromotionRuleDto> Rules,
    IReadOnlyList<string> GroupSkus, string? CategoryCode, int GroupMinQuantity, decimal GroupPercent);

public interface IPricingModule
{
    Task<QuoteDto> QuoteAsync(string retailerCode, IReadOnlyList<QuoteLineInput> lines, DateOnly onDate, CancellationToken ct = default);
    /// <summary>Batch code → discount percent (KMB) in force on the date. Applied by Ordering to the allocated batches.</summary>
    Task<IReadOnlyDictionary<string, decimal>> ActiveBatchDiscountsAsync(DateOnly onDate, CancellationToken ct = default);
    Task<IReadOnlyList<PromotionSnapshotDto>> ActivePromotionsAsync(DateOnly onDate, CancellationToken ct = default);
}
