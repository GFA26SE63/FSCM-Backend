namespace FSCM.Application.Features.QrLabels;

public static class QrLabelStatuses
{
    public const string Available = "Available";
    public const string Picked = "Picked";
    public const string InTransit = "InTransit";
    public const string Delivered = "Delivered";
    public const string Invalidated = "Invalidated";
}

public static class QrLabelEventTypes
{
    public const string Generated = "Generated";
    public const string Split = "Split";
    public const string Picked = "Picked";
    public const string Inspected = "Inspected";
    public const string Issued = "Issued";
    public const string Received = "Received";
    public const string Dispatched = "Dispatched";
    public const string RetailerReceived = "RetailerReceived";
    public const string Invalidated = "Invalidated";
}
