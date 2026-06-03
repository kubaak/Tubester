using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tubester.Abstractions.Analytics;

namespace Tubester.Persistence.Analytics;

public class UserEventLogger(TubesterDb tubesterDb) : IUserEventLogger
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

        await tubesterDb.UserEvents.AddAsync(userEvent, cancellationToken);
        await tubesterDb.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteByUserIdAsync(
        string userId,
        UserEventType deletionEventType,
        DateTimeOffset deletedAt,
        CancellationToken cancellationToken)
    {
        // First log the deletion audit event before deleting
        await LogAsync(userId, deletionEventType, cancellationToken: cancellationToken);

        // Delete all other user events using raw SQL to avoid EF tracking issues
        await tubesterDb.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM "analytics"."UserEvents"
            WHERE "UserId" = {userId}
              AND "EventType" != {deletionEventType.ToString()}
            """,
            cancellationToken);
    }
}
