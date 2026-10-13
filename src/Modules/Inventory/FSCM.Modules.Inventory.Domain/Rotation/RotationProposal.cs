using FSCM.SharedKernel.Domain;

namespace FSCM.Modules.Inventory.Domain.Rotation;

public enum ProposalStatus { Proposed, Approved, Skipped, Expired }

/// <summary>One line of the nightly rotation result shown in Portal → "Luân chuyển cận hạn". Approving it creates a Transfer (DC) of kind NearExpiryRotation.</summary>
public sealed class RotationProposal : AggregateRoot<Guid>
{
    private RotationProposal() { }
    public RotationProposal(DateOnly runDate, string batchCode, string from, string to, int excess, int receivingCapacity, int quantity, decimal score, decimal salvage, decimal tripCost, string traceJson)
    {
        Id = Guid.NewGuid(); RunDate = runDate; BatchCode = batchCode; FromWarehouse = from; ToWarehouse = to; Excess = excess; ReceivingCapacity = receivingCapacity;
        Quantity = quantity; Score = score; SalvageValue = salvage; TripCost = tripCost; TraceJson = traceJson; Status = ProposalStatus.Proposed;
    }
    public DateOnly RunDate { get; private set; }
    public string BatchCode { get; private set; } = "";
    public string FromWarehouse { get; private set; } = "";
    public string ToWarehouse { get; private set; } = "";
    public int Excess { get; private set; }
    public int ReceivingCapacity { get; private set; }
    public int Quantity { get; private set; }
    public decimal Score { get; private set; }
    public decimal SalvageValue { get; private set; }
    public decimal TripCost { get; private set; }
    /// <summary>Full 4-step explanation (windows, filter, scores, turns) for the detail screen.</summary>
    public string TraceJson { get; private set; } = "{}";
    public ProposalStatus Status { get; private set; }
    public string? DecidedBy { get; private set; }
    public string? TransferCode { get; private set; }

    public void ChangeQuantity(int qty, int minQty)
    {
        Guard.Against(Status != ProposalStatus.Proposed, "proposal.state", "Chỉ sửa đề xuất chưa xử lý.");
        Guard.Against(qty < minQty || qty > Math.Min(Excess, ReceivingCapacity), "proposal.qty", $"Số lượng từ {minQty} đến {Math.Min(Excess, ReceivingCapacity)} thùng.");
        var unit = SalvageValue / Math.Max(1, Quantity); Quantity = qty; SalvageValue = unit * qty;
    }
    public void Approve(string by, string transferCode) { Guard.Against(Status != ProposalStatus.Proposed, "proposal.state", "Đề xuất đã được xử lý."); Status = ProposalStatus.Approved; DecidedBy = by; TransferCode = transferCode; }
    public void Skip(string by) { Guard.Against(Status != ProposalStatus.Proposed, "proposal.state", "Đề xuất đã được xử lý."); Status = ProposalStatus.Skipped; DecidedBy = by; }
}
