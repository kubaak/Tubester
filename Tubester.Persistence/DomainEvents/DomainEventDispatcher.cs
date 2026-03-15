using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tubester.Abstractions.DomainEvents;
using Tubester.Domain;

namespace Tubester.Persistence.DomainEvents;

public sealed class DomainEventDispatcher(
    IServiceProvider serviceProvider,
    ILogger<DomainEventDispatcher> logger) : IDomainEventDispatcher
{
    public async Task DispatchAsync(IReadOnlyList<IDomainEvent> domainEvents, CancellationToken cancellationToken)
    {
        foreach (var domainEvent in domainEvents)
        {
            var eventType = domainEvent.GetType();
            var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(eventType);
            var handlers = serviceProvider.GetServices(handlerType);

            foreach (var handler in handlers)
            {
                try
                {
                    var method = handlerType.GetMethod(nameof(IDomainEventHandler<IDomainEvent>.HandleAsync));
                    if (method is null)
                    {
                        logger.LogError("HandleAsync method not found on handler {HandlerType} for event {EventType}",
                            handler?.GetType().Name, eventType.Name);
                        continue;
                    }
                    
                    if (method.Invoke(handler, [domainEvent, cancellationToken]) is Task task)
                    {
                        await task;
                    }
                }
                catch (Exception exception)
                {
                    logger.LogError(exception,
                        "Domain event handler {HandlerType} failed for event {EventType}",
                        handler?.GetType().Name, eventType.Name);
                }
            }
        }
    }
}
