using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Tubester.Abstractions.DomainEvents;
using Tubester.Domain;
using Tubester.Persistence.DomainEvents;
using Xunit;

namespace Tubester.IntegrationTests.DomainEvents;

public class DomainEventDispatcherTests
{
    private sealed record TestEvent(DateTimeOffset OccurredAtUtc) : IDomainEvent;

    private sealed record UnhandledEvent(DateTimeOffset OccurredAtUtc) : IDomainEvent;

    private sealed class TestHandler : IDomainEventHandler<TestEvent>
    {
        public List<TestEvent> HandledEvents { get; } = [];

        public Task HandleAsync(TestEvent domainEvent, CancellationToken cancellationToken)
        {
            HandledEvents.Add(domainEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingHandler : IDomainEventHandler<TestEvent>
    {
        public Task HandleAsync(TestEvent domainEvent, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Handler failed.");
        }
    }

    [Fact]
    public async Task DispatchAsync_InvokesRegisteredHandler()
    {
        var handler = new TestHandler();
        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventHandler<TestEvent>>(handler);
        var serviceProvider = services.BuildServiceProvider();
        var logger = new Mock<ILogger<DomainEventDispatcher>>();
        var dispatcher = new DomainEventDispatcher(serviceProvider, logger.Object);

        var domainEvent = new TestEvent(DateTimeOffset.UtcNow);
        await dispatcher.DispatchAsync([domainEvent], CancellationToken.None);

        Assert.Single(handler.HandledEvents);
        Assert.Same(domainEvent, handler.HandledEvents[0]);
    }

    [Fact]
    public async Task DispatchAsync_NoHandlerRegistered_DoesNotThrow()
    {
        var services = new ServiceCollection();
        var serviceProvider = services.BuildServiceProvider();
        var logger = new Mock<ILogger<DomainEventDispatcher>>();
        var dispatcher = new DomainEventDispatcher(serviceProvider, logger.Object);

        var domainEvent = new UnhandledEvent(DateTimeOffset.UtcNow);
        await dispatcher.DispatchAsync([domainEvent], CancellationToken.None);
    }

    [Fact]
    public async Task DispatchAsync_HandlerThrows_LogsErrorAndContinues()
    {
        var throwingHandler = new ThrowingHandler();
        var successHandler = new TestHandler();
        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventHandler<TestEvent>>(throwingHandler);
        services.AddSingleton<IDomainEventHandler<TestEvent>>(successHandler);
        var serviceProvider = services.BuildServiceProvider();
        var logger = new Mock<ILogger<DomainEventDispatcher>>();
        var dispatcher = new DomainEventDispatcher(serviceProvider, logger.Object);

        var domainEvent = new TestEvent(DateTimeOffset.UtcNow);
        await dispatcher.DispatchAsync([domainEvent], CancellationToken.None);

        Assert.Single(successHandler.HandledEvents);
    }

    [Fact]
    public async Task DispatchAsync_MultipleEvents_DispatchesAll()
    {
        var handler = new TestHandler();
        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventHandler<TestEvent>>(handler);
        var serviceProvider = services.BuildServiceProvider();
        var logger = new Mock<ILogger<DomainEventDispatcher>>();
        var dispatcher = new DomainEventDispatcher(serviceProvider, logger.Object);

        var now = DateTimeOffset.UtcNow;
        var events = new List<IDomainEvent>
        {
            new TestEvent(now),
            new TestEvent(now.AddMinutes(1)),
            new TestEvent(now.AddMinutes(2))
        };

        await dispatcher.DispatchAsync(events, CancellationToken.None);

        Assert.Equal(3, handler.HandledEvents.Count);
    }

    [Fact]
    public async Task DispatchAsync_EmptyEventList_DoesNothing()
    {
        var services = new ServiceCollection();
        var serviceProvider = services.BuildServiceProvider();
        var logger = new Mock<ILogger<DomainEventDispatcher>>();
        var dispatcher = new DomainEventDispatcher(serviceProvider, logger.Object);

        await dispatcher.DispatchAsync([], CancellationToken.None);
    }
}
