using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using Tubester.Abstractions.DomainEvents;
using Tubester.Domain;
using Tubester.Persistence.DomainEvents;
using Xunit;

namespace Tubester.IntegrationTests.DomainEvents;

public class DomainEventInterceptorTests
{
    private sealed record TestEvent(DateTimeOffset OccurredAtUtc) : IDomainEvent;

    private sealed class TestEntity : Entity
    {
        public int Id { get; set; }
        public void RaiseTestEvent(DateTimeOffset occurredAtUtc) => Raise(new TestEvent(occurredAtUtc));
    }

    private sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
    {
        public DbSet<TestEntity> TestEntities => Set<TestEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<TestEntity>(entity =>
            {
                entity.HasKey(testEntity => testEntity.Id);
                entity.Ignore(testEntity => testEntity.DomainEvents);
            });
        }
    }

    private static TestDbContext CreateInMemoryContext(DomainEventInterceptor interceptor)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(interceptor)
            .Options;
        return new TestDbContext(options);
    }

    [Fact]
    public async Task SaveChangesAsync_DispatchesDomainEventsAfterSave()
    {
        var dispatchedEvents = new List<IDomainEvent>();
        var mockDispatcher = new Mock<IDomainEventDispatcher>();
        mockDispatcher
            .Setup(dispatcher => dispatcher.DispatchAsync(It.IsAny<IReadOnlyList<IDomainEvent>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<IDomainEvent>, CancellationToken>((events, _) => dispatchedEvents.AddRange(events))
            .Returns(Task.CompletedTask);

        var interceptor = new DomainEventInterceptor(mockDispatcher.Object);
        using var context = CreateInMemoryContext(interceptor);

        var entity = new TestEntity { Id = 1 };
        entity.RaiseTestEvent(DateTimeOffset.UtcNow);

        context.TestEntities.Add(entity);
        await context.SaveChangesAsync();

        Assert.Single(dispatchedEvents);
        Assert.IsType<TestEvent>(dispatchedEvents[0]);
    }

    [Fact]
    public async Task SaveChangesAsync_ClearsEventsAfterDispatch()
    {
        var mockDispatcher = new Mock<IDomainEventDispatcher>();
        mockDispatcher
            .Setup(dispatcher => dispatcher.DispatchAsync(It.IsAny<IReadOnlyList<IDomainEvent>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var interceptor = new DomainEventInterceptor(mockDispatcher.Object);
        using var context = CreateInMemoryContext(interceptor);

        var entity = new TestEntity { Id = 1 };
        entity.RaiseTestEvent(DateTimeOffset.UtcNow);

        context.TestEntities.Add(entity);
        await context.SaveChangesAsync();

        Assert.Empty(entity.DomainEvents);
    }

    [Fact]
    public async Task SaveChangesAsync_NoEvents_DoesNotDispatch()
    {
        var mockDispatcher = new Mock<IDomainEventDispatcher>();
        var interceptor = new DomainEventInterceptor(mockDispatcher.Object);
        using var context = CreateInMemoryContext(interceptor);

        var entity = new TestEntity { Id = 1 };
        context.TestEntities.Add(entity);
        await context.SaveChangesAsync();

        mockDispatcher.Verify(
            dispatcher => dispatcher.DispatchAsync(It.IsAny<IReadOnlyList<IDomainEvent>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
