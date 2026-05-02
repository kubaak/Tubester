using Tubester.Domain;

namespace Tubester.Abstractions.Videos;

public interface IVideoRepository
{
    Task<List<Video>> GetCommentableVideosAsync(string uploadPlaylistId, CancellationToken cancellationToken);

    /// <summary>
    /// Gets a video by its ID from the database.
    /// </summary>
    /// <param name="uploadPlaylistId">Upload playlist ID to filter videos by.</param>
    /// <param name="videoId">The video ID to search for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The video if found, null otherwise.</returns>
    Task<Video?> GetVideoByIdAsync(string uploadPlaylistId, string videoId, CancellationToken cancellationToken);

    Task<(int inserted, int updated)> UpsertAsync(
        string uploadPlaylistId,
        IEnumerable<Video> videos,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a page of videos with optional title filtering and cursor-based pagination for a specific channel.
    /// </summary>
    /// <param name="uploadPlaylistId">Upload playlist ID to filter videos by.</param>
    /// <param name="title">Optional title filter (case-insensitive substring match).</param>
    /// <param name="visibilities">Optional set of visibilities to include.</param>
    /// <param name="afterPublishedAtUtc">Cursor: published date to search after (exclusive).</param>
    /// <param name="afterVideoId">Cursor: video ID to search after when published dates are equal.</param>
    /// <param name="take">Maximum number of items to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of videos ordered by PublishedAt DESC, VideoId DESC.</returns>
    Task<List<Video>> GetVideosPageAsync(
        string uploadPlaylistId,
        string? title,
        IReadOnlyCollection<VideoVisibility>? visibilities,
        DateTimeOffset? afterPublishedAtUtc,
        string? afterVideoId,
        int take,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets ETags for specified video IDs to support conditional requests.
    /// </summary>
    /// <param name="uploadPlaylistId"></param>
    /// <param name="videoIds">Video IDs to get ETags for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Dictionary mapping video ID to ETag.</returns>
    Task<Dictionary<string, string?>> GetVideoETagsAsync(
        string uploadPlaylistId,
        IEnumerable<string> videoIds,
        CancellationToken cancellationToken);

    Task MarkCommentsDisabledAsync(string uploadPlaylistId, string videoId, CancellationToken cancellationToken);

    Task<bool> TryAddAiOperationsInProgressAsync(
        string uploadPlaylistId,
        string videoId,
        AiVideoOperationFlags operations,
        CancellationToken cancellationToken);

    Task<bool> TryClearAiOperationsInProgressAsync(
        string uploadPlaylistId,
        string videoId,
        AiVideoOperationFlags operations,
        CancellationToken cancellationToken);
}
