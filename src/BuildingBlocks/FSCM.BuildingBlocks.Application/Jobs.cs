namespace FSCM.BuildingBlocks.Application;

/// <summary>
/// A job that runs once a day. Lives in the application layer so modules can declare jobs without
/// depending on infrastructure. Hosted by DailyJobScheduler (local/dev) or an Azure Functions Timer Trigger (prod).
/// </summary>
public interface IScheduledJob
{
    string Name { get; }
    /// <summary>Local time (Asia/Ho_Chi_Minh) to run every day.</summary>
    TimeOnly RunAt { get; }
    Task RunAsync(CancellationToken ct);
}
