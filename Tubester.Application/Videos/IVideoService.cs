using Tubester.Application.Contracts;
using Tubester.Application.Contracts.Videos;
using Tubester.Domain;

namespace Tubester.Application.Videos;

public interface IVideoService
{
    /// <summary>
    /// Gets a paginated list of videos with optional title filtering.
    /// </summary>
    /// <param name="title">Optional case-insensitive substring filter for video titles.</param>
    /// <param name="visibility">Optional array of visibility filters.</param>
    /// <param name="pageSize">Number of items per page (1-100, defaults to configured DefaultPageSize).</param>
    /// <param name="pageToken">Cursor token for pagination, or null for first page.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Paginated result containing videos and optional next page token.</returns>
    /// <exception cref="Exceptions.InvalidPageSizeException">When pageSize is outside valid range.</exception>
    /// <exception cref="Exceptions.InvalidPageTokenException">When pageToken is malformed.</exception>
    Task<PagedResult<VideoListItemDto>> GetVideosAsync(string? title, VideoVisibility[]? visibility, int? pageSize,
        string? pageToken, CancellationToken ct);

    Task<VideoDetailsDto?> GetVideoDetailsAsync(string videoId, CancellationToken cancellationToken);

    Task<CopyVideoTemplateResult> CopyTemplateAsync(
        string userId,
        CopyVideoTemplateRequest request,
        CancellationToken cancellationToken);

    Task<VideoDetailsDto?> UpdateVideoMetadataAsync(
        string userId,
        UpdateVideoMetadataRequest request,
        CancellationToken cancellationToken);
}