using FSCM.SharedKernel.Domain;

namespace FSCM.Modules.Performance.Domain.Facts;

public sealed class FactLine
{
    private FactLine() { }
    public FactLine(string sku, int quantity, decimal gross, decimal discount, decimal net) { Sku = sku; Quantity = quantity; Gross = gross; Discount = discount; Net = net; }
    public string Sku { get; private set; } = "";
    public int Quantity { get; private set; }
    public decimal Gross { get; private set; }
    public decimal Discount { get; private set; }
    public decimal Net { get; private set; }
}

/// <summary>
/// Read model row written when Ordering publishes OrderCompleted. SalesUserId is null when the order was not closed by a Sales
/// (created by Operator / Admin) – those orders count in the dashboard but never in a Sales ranking or KPI.
/// </summary>
public sealed class SalesOrderFact : Entity<string>
{
    private readonly List<FactLine> _lines = new();
    private SalesOrderFact() { }
    public SalesOrderFact(string orderCode, string? salesUserId, string retailerCode, string paymentMethod, DateOnly completedOn, decimal gross, decimal discount, decimal net, IEnumerable<FactLine> lines)
    {
        Id = orderCode; SalesUserId = salesUserId; RetailerCode = retailerCode; PaymentMethod = paymentMethod; CompletedOn = completedOn; Gross = gross; Discount = discount; Net = net;
        _lines.AddRange(lines);
    }
    public string? SalesUserId { get; private set; }
    public string RetailerCode { get; private set; } = "";
    public string PaymentMethod { get; private set; } = "";
    public DateOnly CompletedOn { get; private set; }
    /// <summary>Before promotion.</summary>
    public decimal Gross { get; private set; }
    public decimal Discount { get; private set; }
    /// <summary>Revenue after promotion – the figure KPI and ranking use.</summary>
    public decimal Net { get; private set; }
    public bool ClosedBySales => SalesUserId is not null;
    public IReadOnlyCollection<FactLine> Lines => _lines.AsReadOnly();
}

/// <summary>A retailer declared by a Sales and approved by Admin (RetailerRegistered) – counts as "new store" in that Sales' KPI.</summary>
public sealed class NewStoreFact : Entity<string>
{
    private NewStoreFact() { }
    public NewStoreFact(string retailerCode, string salesUserId, DateOnly registeredOn) { Id = retailerCode; SalesUserId = salesUserId; RegisteredOn = registeredOn; }
    public string SalesUserId { get; private set; } = "";
    public DateOnly RegisteredOn { get; private set; }
}
