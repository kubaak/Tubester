namespace Tubester.Abstractions.Analytics;

/// <summary>
/// Logs user events for analytics and auditing.
/// </summary>
public interface IUserEventLogger
{
    /// <summary>
    /// Logs a user event with optional video, comment and metadata information.
    /// </summary>
    /// <param name="userId">Identifier of the user who triggered the event.</param>
    /// <param name="eventType">Type of event that occurred.</param>
    /// <param name="videoId">Optional identifier of the related video.</param>
    /// <param name="commentId">Optional identifier of the related comment.</param>
    /// <param name="metadata">Optional additional metadata describing the event.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    Task LogAsync(
        string userId,
        UserEventType eventType,
        string? videoId = null,
        string? commentId = null,
        object? metadata = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all user events for a user, preserving only security audit events.
    /// </summary>
    /// <param name="userId">The user ID whose events should be deleted.</param>
    /// <param name="deletionEventType">The event type to use for the deletion audit event.</param>
    /// <param name="deletedAt">The timestamp when the deletion occurred.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    Task DeleteByUserIdAsync(
        string userId,
        UserEventType deletionEventType,
        DateTimeOffset deletedAt,
        CancellationToken cancellationToken);
}
