namespace Tubester.Domain.Events;

/// <summary>
/// Raised when a reply is approved by the user. No handlers registered yet — placeholder for future analytics.
/// </summary>
public sealed record ReplyApprovedEvent(
    string ActorUserId,
    string CommentId,
    string VideoId,
    string FinalText,
    DateTimeOffset OccurredAtUtc) : IDomainEvent;
