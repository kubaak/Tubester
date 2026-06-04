using Tubester.Domain;

namespace Tubester.Abstractions.Videos;

/// <summary>
/// Represents a repository interface for managing video entities and related operations.
/// </summary>
public interface IVideoRepository
{
    /// <summary>
    /// Retrieves a list of videos that can be commented on from the specified upload playlist.
    /// </summary>
    /// <param name="uploadPlaylistId">The ID of the upload playlist to filter videos by.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A list of videos that are eligible for comments.</returns>
    Task<List<Video>> GetCommentableVideosAsync(string uploadPlaylistId, CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves a list of videos that are not in sync with YouTube.
    /// </summary>
    /// <param name="uploadPlaylistId">The ID of the upload playlist to filter videos by.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A list of videos requiring updates or manual intervention.</returns>
    Task<List<VideoListItemDto>> GetDirtyVideosAsync(string uploadPlaylistId, CancellationToken cancellationToken);

    /// <summary>
    /// Gets a video by its ID from the database.
    /// </summary>
    /// <param name="uploadPlaylistId">Upload playlist ID to filter videos by.</param>
    /// <param name="videoId">The video ID to search for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The video if found, null otherwise.</returns>
    Task<Video?> GetVideoByIdAsync(string uploadPlaylistId, string videoId, CancellationToken cancellationToken);

    /// <summary>
    /// Updates existing videos in the specified upload playlist.
    /// </summary>
    /// <param name="uploadPlaylistId">The ID of the upload playlist containing the videos to be updated.</param>
    /// <param name="videos">The collection of videos to be evaluated for updates.</param>
    /// <param name="applyUpdate">A function to determine whether an update should be applied. Takes the existing video, the new video, and the current timestamp as parameters.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>The number of videos successfully updated.</returns>
    Task<int> UpdateExistingAsync(
        string uploadPlaylistId,
        IEnumerable<Video> videos,
        Func<Video, Video, DateTimeOffset, bool> applyUpdate,
        CancellationToken cancellationToken);

    /// <summary>
    /// Upserts a list of videos with data from YouTube.
    /// </summary>
    /// <param name="uploadPlaylistId"></param>
    /// <param name="videos"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task<(int inserted, int updated, HashSet<string> syncedVideoIds)> UpsertRemoteSyncAsync(
        string uploadPlaylistId,
        IEnumerable<Video> videos,
        CancellationToken cancellationToken);

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

    /// <summary>
    /// Marks the comments as disabled for a specific video in the given upload playlist.
    /// </summary>
    /// <param name="uploadPlaylistId">The ID of the upload playlist that contains the video.</param>
    /// <param name="videoId">The ID of the video for which comments should be disabled.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task MarkCommentsDisabledAsync(string uploadPlaylistId, string videoId, CancellationToken cancellationToken);

    /// <summary>
    /// Attempts to add AI operations that are in progress for the specified video within the given upload playlist.
    /// </summary>
    /// <param name="uploadPlaylistId">The ID of the upload playlist that contains the video.</param>
    /// <param name="videoId">The ID of the video for which AI operations are being tracked.</param>
    /// <param name="operations">The set of AI operations to mark as in progress.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A boolean indicating whether the AI operations were successfully added as in progress.</returns>
    Task<bool> TryAddAiOperationsInProgressAsync(
        string uploadPlaylistId,
        string videoId,
        AiVideoOperationFlags operations,
        CancellationToken cancellationToken);

    /// <summary>
    /// Attempts to clear specified AI operations in progress for a given video within an upload playlist.
    /// </summary>
    /// <param name="uploadPlaylistId">The unique identifier of the upload playlist containing the video.</param>
    /// <param name="videoId">The unique identifier of the video for which AI operations are being cleared.</param>
    /// <param name="operations">The AI operations to be cleared, specified using a combination of flags.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>True if the operations are successfully cleared; otherwise, false.</returns>
    Task<bool> TryClearAiOperationsInProgressAsync(
        string uploadPlaylistId,
        string videoId,
        AiVideoOperationFlags operations,
        CancellationToken cancellationToken);
}
