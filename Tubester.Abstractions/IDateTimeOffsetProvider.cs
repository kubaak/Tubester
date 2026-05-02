namespace Tubester.Abstractions;

public interface IDateTimeOffsetProvider
{
    DateTimeOffset GetUtcNowDateTimeOffset();
}