using System.Text.Json;
using Tubester.Abstractions.Analytics;

namespace Tubester.Persistence.Analytics;

public class UserEventLogger(TubesterDb TubesterDb) : IUserEventLogger
{
    public async Task LogAsync(
        string userId,
        UserEventType eventType,
        string? videoId = null,
        string? commentId = null,
        object? metadata = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("User id is required.", nameof(userId));
        }

        var occurredAtUtc = DateTimeOffset.UtcNow;
        string? metadataJson = null;

        if (metadata is not null)
        {
            metadataJson = JsonSerializer.Serialize(metadata);
        }

        var userEvent = new UserEvent
        {
            UserId = userId,
            OccurredAtUtc = occurredAtUtc,
            EventType = eventType.ToString(),
            VideoId = videoId,
            CommentId = commentId,
            MetadataJson = metadataJson
        };

        await TubesterDb.UserEvents.AddAsync(userEvent, cancellationToken);
        await TubesterDb.SaveChangesAsync(cancellationToken);
    }
}