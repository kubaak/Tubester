using Tubester.Domain.Events;
using ArgumentException = System.ArgumentException;

namespace Tubester.Domain;

public enum ReplyStatus { Pulled = 0, Suggested = 1, Approved = 2, Posted = 3, Ignored = 4, Drafting = 5 }
public class Reply : Entity
{
    public string CommentId { get; private set; }
    public string VideoId { get; private set; }
    public string VideoTitle { get; private set; }
    public string CommentText { get; private set; }
    public ReplyStatus Status { get; private set; }
    public string? SuggestedText { get; private set; }
    public string? FinalText { get; private set; }
    public DateTimeOffset PulledAt { get; private set; }
    public DateTimeOffset? SuggestedAt { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }
    public DateTimeOffset? PostedAt { get; private set; }

    public string ThumbnailUrl => $"https://i.ytimg.com/vi/{VideoId}/sddefault.jpg";

    private const int MaxLength = 10_000; //limit for the youTube comment

    public void SuggestText(string text, DateTimeOffset? suggestedAt)
    {
        EnsureNotPosted();
        SuggestedText = SanitizeText(text);
        SuggestedAt = suggestedAt;
        Status = ReplyStatus.Suggested;
    }

    public void ApproveText(string actorUserId, string finalText, DateTimeOffset? approvedAt)
    {
        EnsureNotPosted();
        if (Status == ReplyStatus.Approved)
        {
            throw new InvalidOperationException("Already approved.");
        }

        FinalText = SanitizeText(finalText);
        ApprovedAt = approvedAt;
        Status = ReplyStatus.Approved;

        if (approvedAt.HasValue)
        {
            Raise(new ReplyApprovedEvent(actorUserId, CommentId, VideoId, FinalText, approvedAt.Value));
        }
    }

    public void Post(string actorUserId, DateTimeOffset postedAt)
    {
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            throw new ArgumentException("Actor user id is required.", nameof(actorUserId));
        }

        EnsureNotPosted();
        if (Status != ReplyStatus.Approved)
        {
            throw new InvalidOperationException("Approve before posting.");
        }

        if (string.IsNullOrWhiteSpace(FinalText))
        {
            throw new InvalidOperationException("FinalText required.");
        }

        PostedAt = postedAt;
        Status = ReplyStatus.Posted;

        Raise(new ReplyPostedEvent(actorUserId, CommentId, VideoId, FinalText!, SuggestedText, postedAt));
    }

    public void Ignore()
    {
        if (Status == ReplyStatus.Posted)
        {
            throw new InvalidOperationException("Already posted.");
        }

        Status = ReplyStatus.Ignored;
    }

    public static Reply Create(string commentId, string videoId, string videoTitle, string commentText, DateTimeOffset pulledAt)
        => new(commentId, videoId, videoTitle, commentText, ReplyStatus.Pulled, pulledAt);

    public static Reply CreateDrafting(string commentId, string videoId, string videoTitle, string commentText, DateTimeOffset pulledAt)
        => new(commentId, videoId, videoTitle, commentText, ReplyStatus.Drafting, pulledAt);

    private Reply(string commentId, string videoId, string videoTitle, string commentText, ReplyStatus status, DateTimeOffset pulledAt)
    {
        CommentId = commentId;
        VideoId = videoId;
        VideoTitle = videoTitle;
        CommentText = commentText;
        PulledAt = pulledAt;
        Status = status;
    }

    private Reply()
    {
    }

    private void EnsureNotPosted()
    {
        if (PostedAt is not null)
        {
            throw new InvalidOperationException("Reply is already posted.");
        }
    }

    private static string SanitizeText(string text)
    {
        var trimmedText = text.Trim();
        return trimmedText.Length switch
        {
            0 => throw new ArgumentException("Reply text cannot be empty."),
            > MaxLength => trimmedText[..MaxLength],
            _ => trimmedText
        };
    }
}