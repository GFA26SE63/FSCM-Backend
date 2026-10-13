namespace FSCM.Domain.Entities;

public sealed class BusinessDocumentSequence
{
    public string DocumentType { get; set; } = string.Empty;
    public DateOnly BusinessDate { get; set; }
    public int LastNumber { get; set; }
}
