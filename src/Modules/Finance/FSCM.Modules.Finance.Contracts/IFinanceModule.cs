using FSCM.BuildingBlocks.Application;

namespace FSCM.Modules.Finance.Contracts;

public sealed record ReceivableDto(string OrderCode, string RetailerCode, string RetailerName, string? SalesUserId, int TermDays,
    DateOnly DeliveredOn, DateOnly DueDate, decimal Amount, string Status, DateOnly? PaidOn, int DaysOverdue);

public interface IFinanceModule
{
    Task<ReceivableDto?> GetReceivableAsync(string orderCode, CancellationToken ct = default);
}

/// <summary>Credit order paid in full (single collection). Loyalty turns Pending points into Earned; Ordering completes the order.</summary>
public sealed record CreditOrderPaid(string OrderCode, string RetailerCode, string? SalesUserId, decimal Amount, DateOnly PaidOn) : IntegrationEvent;
public sealed record ReceivableOverdue(string OrderCode, string RetailerCode, string? SalesUserId, decimal Amount, DateOnly DueDate, int DaysOverdue) : IntegrationEvent;
