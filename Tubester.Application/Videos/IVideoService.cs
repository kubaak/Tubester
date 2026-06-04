using Tubester.Abstractions.Videos;
using Tubester.Application.Contracts;
using Tubester.Application.Contracts.Videos;

namespace Tubester.Application.Videos;

public interface IVideoService
{
    /// <summary>
    /// Gets a paginated list of videos with optional title filtering.
    /// </summary>
    /// <param name="request">Filter parameters for video listing.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Paginated result containing videos and optional next page token.</returns>
    /// <exception cref="Exceptions.InvalidPageSizeException">When pageSize is outside valid range.</exception>
    /// <exception cref="Exceptions.InvalidPageTokenException">When pageToken is malformed.</exception>
    Task<PagedResult<VideoListItemDto>> GetVideosAsync(GetVideosRequest request, CancellationToken ct);

    /// <summary>
    /// Retrieves a list of videos that are not in sync with YouTube.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to signal request cancellation.</param>
    /// <returns>A list of video items requiring cleanup or further attention.</returns>
    Task<List<VideoListItemDto>> GetDirtyVideosAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves detailed metadata of a specific video by its unique identifier.
    /// </summary>
    /// <param name="videoId">The unique identifier of the video to retrieve details for.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>An object containing detailed metadata of the video, or null if no video is found.</returns>
    /// <exception cref="ArgumentNullException">Thrown when videoId is null or empty.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is canceled.</exception>
    Task<VideoDetailsDto?> GetVideoDetailsAsync(string videoId, CancellationToken cancellationToken);

    /// <summary>
    /// Copies a video metadata from a source video to a target video, applying specific attributes according to the request parameters.
    /// </summary>
    /// <param name="userId">The identifier of the user initiating the template copy operation.</param>
    /// <param name="request">The request containing source video ID, target video ID, and the attributes to be copied.</param>
    /// <param name="cancellationToken">Token to observe while waiting for the task to complete.</param>
    /// <returns>An object containing details of the copied template, such as source and target video IDs, applied attributes, and final metadata.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="userId"/> or <paramref name="request"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the source and target video IDs are identical or when required attributes cannot be copied.</exception>
    /// <exception cref="OperationCanceledException">Thrown if the operation is canceled via the provided cancellation token.</exception>
    Task<CopyVideoTemplateResult> CopyTemplateAsync(
        string userId,
        CopyVideoTemplateRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Saves or updates draft metadata for a video, including title, description, tags, and playlists.
    /// </summary>
    /// <param name="userId">The identifier of the user saving the draft.</param>
    /// <param name="request">The data for the video draft, including title, description, tags, and playlist IDs.</param>
    /// <param name="cancellationToken">Token to signal the operation should be canceled.</param>
    /// <returns>The updated video details, or null if the operation fails.</returns>
    /// <exception cref="ArgumentNullException">Thrown when required parameters are null or empty.</exception>
    /// <exception cref="Exceptions.VideoNotFoundException">Thrown when the specified video is not found.</exception>
    /// <exception cref="Exceptions.UnauthorizedAccessException">Thrown when the user does not have permission to modify the video.</exception>
    Task<VideoDetailsDto?> SaveDraftMetadataAsync(
        string userId,
        SaveVideoDraftRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Resynchronizes the metadata of a video with the latest data from Youtube.
    /// </summary>
    /// <param name="videoId">The unique identifier of the video to be resynchronized.</param>
    /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
    /// <returns>The updated video details if the resynchronization is successful; otherwise, null.</returns>
    /// <exception cref="ArgumentException">Thrown when the videoId is null, empty, or consists only of whitespace.</exception>
    /// <exception cref="Exceptions.VideoNotFoundException">Thrown when a video with the specified videoId does not exist.</exception>
    /// <exception cref="Exceptions.ExternalServiceException">Thrown when an error occurs while communicating with the external source.</exception>
    Task<VideoDetailsDto?> ResyncVideoAsync(
        string videoId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Updates metadata for a specific video.
    /// </summary>
    /// <param name="userId">The ID of the user initiating the update operation.</param>
    /// <param name="operationId">A unique identifier for the update operation.</param>
    /// <param name="request">The updated metadata for the video, including its ID.</param>
    /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
    /// <returns>The updated video details, or null if the video could not be updated.</returns>
    /// <exception cref="ArgumentNullException">When userId, operationId, or request is null or empty.</exception>
    /// <exception cref="Exceptions.VideoNotFoundException">When the video specified in the request is not found.</exception>
    Task<VideoDetailsDto?> UpdateVideoAsync(
        string userId,
        string operationId,
        UpdateVideoMetadataRequest request,
        CancellationToken cancellationToken);
}
