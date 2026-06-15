using Tubester.Abstractions;
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
        string commentText,
        string language,
        IReadOnlyList<RelevantReplyExample>? relevantExamples);

    string BuildPlaylistPrompt(
        PlaylistSuggestionContext context,
        IReadOnlyList<PlaylistCandidateDto> playlists);
}
