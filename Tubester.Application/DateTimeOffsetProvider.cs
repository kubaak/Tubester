using Tubester.Abstractions;

namespace Tubester.Application;

public class DateTimeOffsetProvider : IDateTimeOffsetProvider
{
    public DateTimeOffset GetUtcNowDateTimeOffset()
    {
        return DateTimeOffset.UtcNow;
    }
}