namespace Tubester.Domain.Events;

/// <summary>
/// Raised when AI-suggested text is assigned to a reply. No handlers registered yet — placeholder for future analytics.
/// </summary>
public sealed record ReplySuggestedEvent(
    string CommentId,
    string VideoId,
    string SuggestedText,
    DateTimeOffset OccurredAtUtc) : IDomainEvent;
