using System.ComponentModel.DataAnnotations;

namespace Tubester.Application.Options;

/// <summary>
/// Configuration options for video listing endpoint.
/// </summary>
public sealed class VideoListingOptions
{
    /// <summary>
    /// Default page size when not specified in request.
    /// </summary>
    [Range(1, 100)]
    public int DefaultPageSize { get; init; } = 30;

    /// <summary>
    /// Maximum allowed page size.
    /// </summary>
    [Range(1, 100)]
    public int MaxPageSize { get; init; } = 100;
}