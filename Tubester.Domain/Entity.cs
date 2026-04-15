namespace Tubester.Domain;

public abstract class Entity
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents;

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    internal void ClearDomainEvents() => _domainEvents.Clear();

    internal void TransferEventsFrom(Entity source)
    {
        _domainEvents.AddRange(source._domainEvents);
        source._domainEvents.Clear();
    }

    protected static void RequireUtc(DateTimeOffset timestamp)
    {
        if (timestamp.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Timestamp must be in UTC.", nameof(timestamp));
        }
    }
}
