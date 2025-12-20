using YouTubester.Domain;

namespace YouTubester.Abstractions.Videos;

public interface IVideoRepository
{
    Task<List<Video>> GetCommentableVideosAsync(string channelId, CancellationToken cancellationToken);

    /// <summary>
    /// Gets a video by its ID from the database.
    /// </summary>
    /// <param name="channelId">Channel id to filter videos by.</param>
    /// <param name="videoId">The video ID to search for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The video if found, null otherwise.</returns>
    Task<Video?> GetVideoByIdAsync(string channelId, string videoId, CancellationToken cancellationToken);

    Task<(int inserted, int updated)> UpsertAsync(
        string channelId,
        IEnumerable<Video> videos,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a page of videos with optional title filtering and cursor-based pagination for a specific channel.
    /// </summary>
    /// <param name="channelId">Channel id to filter videos by.</param>
    /// <param name="title">Optional title filter (case-insensitive substring match).</param>
    /// <param name="visibilities">Optional set of visibilities to include.</param>
    /// <param name="afterPublishedAtUtc">Cursor: published date to search after (exclusive).</param>
    /// <param name="afterVideoId">Cursor: video ID to search after when published dates are equal.</param>
    /// <param name="take">Maximum number of items to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of videos ordered by PublishedAt DESC, VideoId DESC.</returns>
    Task<List<Video>> GetVideosPageAsync(
        string channelId,
        string? title,
        IReadOnlyCollection<VideoVisibility>? visibilities,
        DateTimeOffset? afterPublishedAtUtc,
        string? afterVideoId,
        int take,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets ETags for specified video IDs to support conditional requests.
    /// </summary>
    /// <param name="channelId"></param>
    /// <param name="videoIds">Video IDs to get ETags for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Dictionary mapping video ID to ETag.</returns>
    Task<Dictionary<string, string?>> GetVideoETagsAsync(
        string channelId,
        IEnumerable<string> videoIds,
        CancellationToken cancellationToken);

    Task MarkCommentsDisabledAsync(string channelId, string videoId, CancellationToken cancellationToken);

    Task<bool> TrySettingAiTemplateInProgressAsync(
        string channelId,
        string videoId,
        bool isAiTemplateInProgress,
        CancellationToken cancellationToken);
}