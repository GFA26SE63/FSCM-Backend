namespace FSCM.Application.Features.Promotions;

public static class PromotionTypes
{
    public const string Campaign = "Campaign";
    public const string BatchDiscount = "BatchDiscount";
}

public static class PromotionAuditActions
{
    public const string Created = "Created";
    public const string Updated = "Updated";
    public const string Activated = "Activated";
    public const string Deactivated = "Deactivated";
    public const string BatchDiscountApplied = "BatchDiscountApplied";
    public const string BatchDiscountRemoved = "BatchDiscountRemoved";
}
