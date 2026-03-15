using Tubester.Domain;
using Xunit;

namespace Tubester.IntegrationTests.DomainEvents;

public class EntityTests
{
    private sealed record TestEvent(DateTimeOffset OccurredAtUtc) : IDomainEvent;

    private sealed class TestEntity : Entity
    {
        public void RaiseTestEvent(DateTimeOffset occurredAtUtc) => Raise(new TestEvent(occurredAtUtc));
    }

    [Fact]
    public void Raise_AddsDomainEvent()
    {
        var entity = new TestEntity();

        entity.RaiseTestEvent(DateTimeOffset.UtcNow);

        Assert.Single(entity.DomainEvents);
    }

    [Fact]
    public void Raise_MultipleTimes_AccumulatesEvents()
    {
        var entity = new TestEntity();
        var now = DateTimeOffset.UtcNow;

        entity.RaiseTestEvent(now);
        entity.RaiseTestEvent(now.AddMinutes(1));
        entity.RaiseTestEvent(now.AddMinutes(2));

        Assert.Equal(3, entity.DomainEvents.Count);
    }

    [Fact]
    public void ClearDomainEvents_RemovesAllEvents()
    {
        var entity = new TestEntity();
        entity.RaiseTestEvent(DateTimeOffset.UtcNow);
        entity.RaiseTestEvent(DateTimeOffset.UtcNow);

        entity.ClearDomainEvents();

        Assert.Empty(entity.DomainEvents);
    }

    [Fact]
    public void DomainEvents_ReturnsReadOnlyList()
    {
        var entity = new TestEntity();

        Assert.IsType<IReadOnlyList<IDomainEvent>>(entity.DomainEvents, exactMatch: false);
    }

    [Fact]
    public void NewEntity_HasNoDomainEvents()
    {
        var entity = new TestEntity();

        Assert.Empty(entity.DomainEvents);
    }
}
