using Pgvector;
using Tubester.Domain.Events;
using ArgumentException = System.ArgumentException;

namespace Tubester.Domain;

public enum ReplyStatus { Pulled = 0, Suggested = 1, Approved = 2, Posted = 3, Ignored = 4, Drafting = 5 }

public class Reply : Entity
{
    public string CommentId { get; private init; } = null!;
    public string VideoId { get; private init; } = null!;
    public string VideoTitle { get; private init; } = null!;
    public string CommentText { get; private init; } = null!;
    public ReplyStatus Status { get; private set; }
    public string? SuggestedText { get; private set; }
    public string? FinalText { get; private set; }
    public DateTimeOffset OriginalCommentAt { get; private set; }
    public DateTimeOffset PulledAt { get; private set; }
    public DateTimeOffset? SuggestedAt { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }
    public DateTimeOffset? PostedAt { get; private set; }

    /// <summary>
    /// Embedding vector for the original comment text, used for RAG-based reply suggestions.
    /// Only populated for approved replies.
    /// </summary>
    public Vector? CommentEmbedding { get; private set; }

    /// <summary>
    /// The embedding model used to generate CommentEmbedding.
    /// </summary>
    public string? CommentEmbeddingModel { get; private set; }

    /// <summary>
    /// When the comment embedding was generated.
    /// </summary>
    public DateTimeOffset? CommentEmbeddingGeneratedAt { get; private set; }

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

    /// <summary>
    /// Sets the comment embedding for RAG-based reply suggestions.
    /// Should only be called for approved replies.
    /// </summary>
    public void SetCommentEmbedding(Vector embedding, string model, DateTimeOffset generatedAt)
    {
        CommentEmbedding = embedding;
        CommentEmbeddingModel = model;
        CommentEmbeddingGeneratedAt = generatedAt;
    }

    public static Reply Create(string commentId, string videoId, string videoTitle, string commentText, DateTimeOffset pulledAt,
        DateTimeOffset originalCommentAt)
        => new(commentId, videoId, videoTitle, commentText, ReplyStatus.Pulled, pulledAt, originalCommentAt);

    public static Reply CreateDrafting(string commentId, string videoId, string videoTitle, string commentText, DateTimeOffset pulledAt,
        DateTimeOffset originalCommentAt)
        => new(commentId, videoId, videoTitle, commentText, ReplyStatus.Drafting, pulledAt, originalCommentAt);

    private Reply(string commentId, string videoId, string videoTitle, string commentText, ReplyStatus status, DateTimeOffset pulledAt,
        DateTimeOffset originalCommentAt)
    {
        CommentId = commentId;
        VideoId = videoId;
        VideoTitle = videoTitle;
        CommentText = commentText;
        PulledAt = pulledAt;
        Status = status;
        OriginalCommentAt = originalCommentAt;
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
