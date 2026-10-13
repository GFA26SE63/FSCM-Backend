using FSCM.SharedKernel.Domain;

namespace FSCM.Modules.Pricing.Domain.BatchDiscounts;

public enum BatchDiscountStatus { Active, Ended }

/// <summary>KMB-yyMMdd-nn: a percentage off one near-expiry batch for a date range, applied by Admin after scanning inventory.</summary>
public sealed class BatchDiscount : AggregateRoot<string>
{
    public const decimal MaxPercent = 60m;
    private BatchDiscount() { }
    public BatchDiscount(string code, string batchCode, string sku, decimal percent, DateOnly from, DateOnly to, string createdBy)
    {
        Guard.Against(percent <= 0 || percent > MaxPercent, "batch_discount.percent", $"% giảm phải trong (0, {MaxPercent:0}].");
        Guard.Against(to < from, "batch_discount.dates", "Ngày kết thúc phải sau ngày bắt đầu.");
        Id = code; BatchCode = batchCode; Sku = sku; Percent = percent; From = from; To = to; CreatedBy = createdBy; Status = BatchDiscountStatus.Active;
    }
    public string BatchCode { get; private set; } = "";
    public string Sku { get; private set; } = "";
    public decimal Percent { get; private set; }
    public DateOnly From { get; private set; }
    public DateOnly To { get; private set; }
    public string CreatedBy { get; private set; } = "";
    public BatchDiscountStatus Status { get; private set; }
    public bool IsActiveOn(DateOnly d) => Status == BatchDiscountStatus.Active && d >= From && d <= To;
    public void End() => Status = BatchDiscountStatus.Ended;
}
