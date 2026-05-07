using Tubester.Abstractions.Playlists;

namespace Tubester.Integration;

/// <summary>
/// Interface for AI client implementations.
/// </summary>
public interface IAiClient
{
    Task<SuggestedMetadata> SuggestMetadataAsync(
        string context, bool generateTitle, bool generateDescription, bool generateTags,
        CancellationToken cancellationToken);

    Task<string?> SuggestReplyAsync(string videoTitle, IEnumerable<string> tags, string commentText,
        string language, CancellationToken cancellationToken);

    Task<IEnumerable<string>> SuggestPlaylistIdsAsync(
        PlaylistSuggestionContext context,
        IReadOnlyList<PlaylistCandidateDto> playlists,
        CancellationToken cancellationToken);
}
