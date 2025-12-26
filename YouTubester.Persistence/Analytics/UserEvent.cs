using System;

namespace YouTubester.Persistence.Analytics;

public class UserEvent
{
    public long Id { get; set; }

    public string UserId { get; set; } = string.Empty;

    public DateTimeOffset OccurredAtUtc { get; set; }

    public string EventType { get; set; } = string.Empty;

    public string? VideoId { get; set; }

    public string? CommentId { get; set; }

    public string? MetadataJson { get; set; }
}