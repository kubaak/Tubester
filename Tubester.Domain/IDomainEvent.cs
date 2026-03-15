namespace Tubester.Domain;

public interface IDomainEvent
{
    DateTimeOffset OccurredAtUtc { get; }
}
