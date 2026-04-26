using System.ComponentModel.DataAnnotations;

namespace Tubester.Application.Options;

/// <summary>
/// Configuration options for AI playlist suggestion.
/// </summary>
public sealed class PlaylistSuggestionOptions
{
    /// <summary>
    /// Maximum number of playlists to include in a single LLM call.
    /// If the channel has more playlists, multiple sequential calls will be made.
    /// </summary>
    [Range(1, 500)]
    public int MaxPlaylistsPerBatch { get; init; } = 50;
}
