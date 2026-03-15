namespace Tubester.Domain.Events;

public sealed record ReplyPostedEvent(
    string ActorUserId,
    string CommentId,
    string VideoId,
    string FinalText,
    string? SuggestedText,
    DateTimeOffset OccurredAtUtc) : IDomainEvent;
