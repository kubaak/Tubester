using Tubester.Domain;

namespace Tubester.Application.Contracts.Videos;

/// <summary>
/// Request for paginated video listing.
/// </summary>
public sealed class GetVideosRequest
{
    /// <summary>
    /// Optional case-insensitive substring filter for video titles.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// Optional visibility filter. Multiple values allowed.
    /// Accepts enum names or numeric values (Public=0, Unlisted=1, Private=2, Scheduled=3).
    /// </summary>
    public VideoVisibility[]? Visibility { get; init; }

    /// <summary>
    /// Items per page (1-100, default 30).
    /// </summary>
    public int? PageSize { get; init; }

    /// <summary>
    /// Cursor token for pagination, or null for first page.
    /// </summary>
    public string? PageToken { get; init; }
}