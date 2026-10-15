namespace FSCM.Application.Features.Payments;

public static class PaymentStatuses
{
    public const string Unpaid = "Unpaid";
    public const string Paid = "Paid";
    public const string Overdue = "Overdue";
}

public static class PaymentCollectionMethods
{
    public const string Cash = "Cash";
    public const string BankTransfer = "BankTransfer";
    public const string DeliverySettlement = "DeliverySettlement";
}

public sealed record CollectPaymentData(
    string CollectionMethod,
    int ExpectedVersion,
    string? Note);

public sealed record PaymentResult(
    Guid Id,
    Guid OrderId,
    string OrderCode,
    Guid RetailerId,
    string RetailerCode,
    string RetailerName,
    Guid SalesUserId,
    string OrderPaymentMethod,
    string Status,
    decimal Amount,
    int CreditTermDays,
    DateOnly DeliveredDate,
    DateOnly? DueDate,
    int DaysOverdue,
    DateOnly? PaidDate,
    string? CollectionMethod,
    Guid? ConfirmedByUserId,
    DateTimeOffset? ConfirmedAt,
    string? Note,
    int Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
