namespace Tubester.Application.Contracts.Replies;

/// <summary>
/// Represents a reply item for listing purposes.
/// </summary>
public sealed record ReplyListItemDto
{
    /// <summary>
    /// YouTube comment ID.
    /// </summary>
    public required string CommentId { get; init; }

    /// <summary>
    /// YouTube video ID associated with the comment.
    /// </summary>
    public required string VideoId { get; init; }

    /// <summary>
    /// Title of the video.
    /// </summary>
    public string? VideoTitle { get; init; }

    /// <summary>
    /// The original comment text.
    /// </summary>
    public string? CommentText { get; init; }

    /// <summary>
    /// Thumbnail URL for the video.
    /// </summary>
    public required string ThumbnailUrl { get; init; }

    /// <summary>
    /// When was the comment made.
    /// </summary>
    public DateTimeOffset? OriginalCommentAt { get; init; }

    /// <summary>
    /// The AI-generated suggested reply text.
    /// </summary>
    public string? SuggestedText { get; init; }
}
