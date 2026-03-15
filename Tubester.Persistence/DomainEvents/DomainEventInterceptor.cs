using Microsoft.EntityFrameworkCore.Diagnostics;
using Tubester.Abstractions.DomainEvents;
using Tubester.Domain;

namespace Tubester.Persistence.DomainEvents;

public sealed class DomainEventInterceptor(IDomainEventDispatcher dispatcher) : SaveChangesInterceptor
{
    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        var context = eventData.Context;
        if (context is null)
        {
            return result;
        }

        var entities = context.ChangeTracker.Entries<Entity>()
            .Select(entry => entry.Entity)
            .Where(entity => entity.DomainEvents.Count > 0)
            .ToList();

        if (entities.Count == 0)
        {
            return result;
        }

        var domainEvents = entities.SelectMany(entity => entity.DomainEvents).ToList();
        entities.ForEach(entity => entity.ClearDomainEvents());

        await dispatcher.DispatchAsync(domainEvents, cancellationToken);

        return result;
    }
}
