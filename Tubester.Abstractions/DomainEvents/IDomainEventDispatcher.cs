using Tubester.Domain;

namespace Tubester.Abstractions.DomainEvents;

public interface IDomainEventDispatcher
{
    Task DispatchAsync(IReadOnlyList<IDomainEvent> domainEvents, CancellationToken cancellationToken);
}
