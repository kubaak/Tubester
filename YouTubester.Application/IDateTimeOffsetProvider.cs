namespace YouTubester.Application;

public interface IDateTimeOffsetProvider
{
    DateTimeOffset GetUtcNowDateTimeOffset();
}