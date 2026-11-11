namespace FSCM.Application.Features.Complaints;

/// <summary>Only the newly created paid surplus is refundable; the rest reduces the source order debt.</summary>
public sealed record RequestSettlement(decimal DebtOffset, decimal Refund)
{
    public static RequestSettlement Calculate(decimal amount, decimal obligation, decimal confirmed, decimal refunded)
    {
        if (amount < 0 || amount > obligation)
            throw ComplaintOperationException.Validation("Giá trị xử lý vượt nghĩa vụ còn lại của đơn.");
        var previousExcess = Math.Max(0, confirmed - refunded - obligation);
        var newExcess = Math.Max(0, confirmed - refunded - (obligation - amount));
        var refund = newExcess - previousExcess;
        return new(amount - refund, refund);
    }
}
