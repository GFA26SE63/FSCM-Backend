namespace FSCM.SharedKernel.Domain;

/// <summary>Base entity. Id is the business code (DH-000123, WH-TA…) or a surrogate key.</summary>
public abstract class Entity<TId> where TId : notnull
{
    private readonly List<IDomainEvent> _domainEvents = new();
    public TId Id { get; protected set; } = default!;
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();
    protected void Raise(IDomainEvent e) => _domainEvents.Add(e);
    public void ClearDomainEvents() => _domainEvents.Clear();
}

/// <summary>Aggregate root with a version column used for optimistic locking.</summary>
public abstract class AggregateRoot<TId> : Entity<TId> where TId : notnull
{
    /// <summary>Incremented on every save; EF maps it as a concurrency token.</summary>
    public int Version { get; protected set; }
    public void Touch() => Version++;
}
