using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Payments;

/// <summary>Authoritative credit eligibility check performed immediately before a credit commitment.</summary>
public sealed class RetailerCreditPolicy(IPaymentStore store, TimeProvider timeProvider)
{
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    public async Task EnsureEligibleAsync(
        Retailer retailer, decimal additionalDebt, CancellationToken cancellationToken)
    {
        if (retailer.Status != "Active")
            throw PaymentOperationException.Conflict("Retailer must be active to use credit.");
        if (retailer.CreditTermDays is not (7 or 14 or 30))
            throw PaymentOperationException.Conflict("Retailer does not have an approved credit term.");
        if (retailer.CreditLimit <= 0)
            throw PaymentOperationException.Conflict("Retailer does not have an available approved credit limit.");
        if (additionalDebt <= 0)
            throw PaymentOperationException.Conflict("Credit commitment amount must be positive.");

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().ToOffset(VietnamOffset).DateTime);
        var outstanding = 0m;
        foreach (var view in await store.ListCreditForRetailerAsync(retailer.Id, cancellationToken))
        {
            if (view.Payment.Status == PaymentStatuses.Cancelled) continue;
            var balance = Math.Max(0, view.Payment.AdjustedAmount - view.Payment.ConfirmedAmount);
            if (balance == 0) continue;
            if (view.Payment.DueDate.HasValue && today > view.Payment.DueDate.Value)
                throw PaymentOperationException.Conflict(
                    "Retailer has an overdue credit receivable and must settle it before using credit again.");
            outstanding += balance;
        }

        if (outstanding + additionalDebt > retailer.CreditLimit)
            throw PaymentOperationException.Conflict(
                "Credit limit would be exceeded by this order. Please use prepaid payment or settle outstanding receivables.");
    }
}
