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

    Task<VideoDetailsDto?> GetVideoDetailsAsync(string videoId, CancellationToken cancellationToken);

    Task<CopyVideoTemplateResult> CopyTemplateAsync(
        string userId,
        CopyVideoTemplateRequest request,
        CancellationToken cancellationToken);

    Task<VideoDetailsDto?> UpdateVideoMetadataAsync(
        string userId,
        string operationId,
        UpdateVideoMetadataRequest request,
        CancellationToken cancellationToken);

    Task<VideoDetailsDto?> SaveDraftMetadataAsync(
        string userId,
        SaveVideoDraftRequest request,
        CancellationToken cancellationToken);

    Task<VideoDetailsDto?> ResyncVideoAsync(
        string videoId,
        CancellationToken cancellationToken);
}
