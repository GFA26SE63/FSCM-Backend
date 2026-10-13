namespace FSCM.BuildingBlocks.Application;

/// <summary>Event published between modules. Written to the outbox in the same transaction, then dispatched by the worker.</summary>
public interface IIntegrationEvent { Guid EventId { get; } DateTime OccurredAtUtc { get; } }

public abstract record IntegrationEvent : IIntegrationEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTime OccurredAtUtc { get; init; } = DateTime.UtcNow;
}

public interface IEventBus { Task PublishAsync<T>(T @event, CancellationToken ct = default) where T : IIntegrationEvent; }

public interface IIntegrationEventHandler<in T> where T : IIntegrationEvent
{
    Task HandleAsync(T @event, CancellationToken ct = default);
}
