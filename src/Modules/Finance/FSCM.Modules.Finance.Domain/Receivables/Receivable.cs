using FSCM.SharedKernel.Domain;

namespace FSCM.Modules.Finance.Domain.Receivables;

public enum ReceivableStatus { Unpaid, Paid, Overdue }

/// <summary>
/// Công nợ of one credit order (partial-credit scope: no credit limit, no partial payments, no aging report).
/// Due date = delivered date + term (7 / 14 / 30 days). The whole amount is collected once.
/// </summary>
public sealed class Receivable : AggregateRoot<string>
{
    private Receivable() { }
    public Receivable(string orderCode, string retailerCode, string? salesUserId, int termDays, DateOnly deliveredOn, decimal amount)
    {
        Guard.Against(termDays is not (7 or 14 or 30), "receivable.term", "Kỳ hạn công nợ chỉ có 7, 14 hoặc 30 ngày.");
        Guard.Against(amount <= 0, "receivable.amount", "Số tiền công nợ phải lớn hơn 0.");
        Id = orderCode; RetailerCode = retailerCode; SalesUserId = salesUserId; TermDays = termDays; DeliveredOn = deliveredOn;
        DueDate = deliveredOn.AddDays(termDays); Amount = amount; Status = ReceivableStatus.Unpaid;
    }
    /// <summary>Order code (DH-000000) – one receivable per credit order.</summary>
    public string RetailerCode { get; private set; } = "";
    public string? SalesUserId { get; private set; }
    public int TermDays { get; private set; }
    public DateOnly DeliveredOn { get; private set; }
    public DateOnly DueDate { get; private set; }
    public decimal Amount { get; private set; }
    public ReceivableStatus Status { get; private set; }
    public DateOnly? PaidOn { get; private set; }
    public string? CollectedBy { get; private set; }

    /// <summary>Overdue from the day after the due date.</summary>
    public bool IsPastDue(DateOnly today) => Status != ReceivableStatus.Paid && today > DueDate;
    public int DaysOverdue(DateOnly today) => IsPastDue(today) ? today.DayNumber - DueDate.DayNumber : 0;
    /// <summary>Status as displayed: an unpaid receivable past its due date is Overdue even before the nightly job flags it.</summary>
    public ReceivableStatus EffectiveStatus(DateOnly today) => Status == ReceivableStatus.Paid ? Status : IsPastDue(today) ? ReceivableStatus.Overdue : Status;

    /// <returns>true when the receivable just became overdue (the job then raises the event once).</returns>
    public bool MarkOverdue(DateOnly today)
    {
        if (Status != ReceivableStatus.Unpaid || !IsPastDue(today)) return false;
        Status = ReceivableStatus.Overdue; return true;
    }

    public void Collect(string by, DateOnly today)
    {
        Guard.Against(Status == ReceivableStatus.Paid, "receivable.paid", $"Đơn {Id} đã thu đủ.");
        Status = ReceivableStatus.Paid; PaidOn = today; CollectedBy = by;
    }
}
