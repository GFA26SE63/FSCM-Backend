namespace FSCM.SharedKernel.Domain;

public interface IDomainEvent { DateTime OccurredAtUtc { get; } }
public abstract record DomainEvent : IDomainEvent { public DateTime OccurredAtUtc { get; init; } = DateTime.UtcNow; }
