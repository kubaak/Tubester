namespace Tubester.Application;

public interface IDateTimeOffsetProvider
{
    DateTimeOffset GetUtcNowDateTimeOffset();
}