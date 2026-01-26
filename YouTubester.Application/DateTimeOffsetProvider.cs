namespace YouTubester.Application;

public class DateTimeOffsetProvider : IDateTimeOffsetProvider
{
    public DateTimeOffset GetUtcNowDateTimeOffset()
    {
        return DateTimeOffset.UtcNow;
    }
}