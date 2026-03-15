using Microsoft.Extensions.DependencyInjection;
using Tubester.Abstractions.DomainEvents;
using Tubester.Application.DomainEvents.Handlers;
using Tubester.Domain.Events;

namespace Tubester.Application.DomainEvents;

public static class DomainEventHandlerRegistration
{
    public static IServiceCollection AddDomainEventHandlers(this IServiceCollection services)
    {
        services.AddScoped<IDomainEventHandler<ReplyPostedEvent>, ReplyPostedAnalyticsHandler>();
        services.AddScoped<IDomainEventHandler<ReplyApprovedEvent>, ReplyApprovedAnalyticsHandler>();
        return services;
    }
}
