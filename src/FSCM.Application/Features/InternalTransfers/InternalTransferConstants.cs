namespace FSCM.Application.Features.InternalTransfers;

public static class InternalTransferStatuses
{
    public const string Requested = "Requested";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
    public const string InTransit = "InTransit";
    public const string Received = "Received";
}

public static class InternalTransferActions
{
    public const string Created = "Created";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
    public const string Dispatched = "Dispatched";
    public const string Received = "Received";
}
