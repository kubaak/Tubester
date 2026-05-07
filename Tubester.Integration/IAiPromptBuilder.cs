using Tubester.Abstractions.Playlists;

namespace Tubester.Integration;

public interface IAiPromptBuilder
{
    string BuildMetadataPrompt(
        string context,
        bool generateTitle,
        bool generateDescription,
        bool generateTags);

    string BuildReplyPrompt(
        string videoTitle,
        IEnumerable<string> tags,
        string commentText,
        string language);

    string BuildPlaylistPrompt(
        PlaylistSuggestionContext context,
        IReadOnlyList<PlaylistCandidateDto> playlists);
}