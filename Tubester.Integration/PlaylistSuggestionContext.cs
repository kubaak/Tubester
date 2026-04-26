namespace Tubester.Integration;

public class PlaylistSuggestionContext
{
    public string PromptEnrichment { get; init; } = null!;
    public required IReadOnlyList<string> LatestPlaylistTitlesUsed { get; init; }
}