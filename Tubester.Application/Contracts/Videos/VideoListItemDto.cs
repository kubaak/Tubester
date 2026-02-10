namespace Tubester.Application.Contracts.Videos;

/// <summary>
/// Represents a video item for listing purposes.
/// </summary>
public sealed record VideoListItemDto
{
    /// <summary>
    /// YouTube video ID.
    /// </summary>
    public required string VideoId { get; init; }

    /// <summary>
    /// Video title.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// Video published date in UTC.
    /// </summary>
    public DateTimeOffset PublishedAt { get; init; }

    /// <summary>
    /// Video thumbnail URL.
    /// </summary>
    public required string ThumbnailUrl { get; init; }
}