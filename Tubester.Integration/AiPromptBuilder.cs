using System.Text.Json;
using Tubester.Abstractions;
using Tubester.Abstractions.Playlists;

namespace Tubester.Integration;

public sealed class AiPromptBuilder : IAiPromptBuilder
{
    public string BuildMetadataPrompt(
        string context,
        bool generateTitle,
        bool generateDescription,
        bool generateTags)
    {
        var requestedFields = new List<string>();
        var rules = new List<string>();
        var jsonProperties = new List<string>();

        if (generateTitle)
        {
            requestedFields.Add("title");
            rules.Add("- Generate a compelling YouTube title");
            jsonProperties.Add("""  "title": "string""");
        }

        if (generateDescription)
        {
            requestedFields.Add("description");
            rules.Add("- Generate a natural, useful, YouTube-friendly description.");
            jsonProperties.Add("""  "description": "string""");
        }

        if (generateTags)
        {
            requestedFields.Add("tags");
            rules.Add("- Generate relevant YouTube search tags.");
            jsonProperties.Add("""  "tags": ["string"]""");
        }

        var requestedFieldsText = string.Join(", ", requestedFields);
        var rulesText = string.Join('\n', rules);
        var schemaJson = string.Join(",\n", jsonProperties);

        return $$"""
                 You are helping a YouTube creator optimize video.

                 Requested fields:
                 {{requestedFieldsText}}

                 Rules:
                 {{rulesText}}

                 Context:
                 {{context}}

                 Required JSON shape:
                 {
                 {{schemaJson}}
                 }
                 """;
    }

    public string BuildReplyPrompt(
        string videoTitle,
        IEnumerable<string> tags,
        string commentText,
        string language)
    {
        var targetLanguage = string.IsNullOrWhiteSpace(language)
            ? "English"
            : language;
        
        return $$"""
                 YouTube reply. JSON only {"reply":"text or null"}
                 Video: {{videoTitle}}
                 Comment: {{commentText}}
                 Language: {{targetLanguage}}
                 Rules: short, friendly, natural, not robotic, no unknown facts; null if spam/hateful/meaningless.
                 """;
    }

    public string BuildPlaylistPrompt(
        PlaylistSuggestionContext context,
        IReadOnlyList<PlaylistCandidateDto> playlists)
    {
        var playlistLines = string.Join(
            "\n",
            playlists.Select((p, i) => $"{i}: {p.Name}"));

        var latestPlaylistTitlesUsed = context.LatestPlaylistTitlesUsed ?? [];

        var latestPlaylistTitlesSection = latestPlaylistTitlesUsed.Count > 0
            ? $"""

                Recently used playlist titles, weak hint only:
                {string.Join(", ", latestPlaylistTitlesUsed)}
                """
            : string.Empty;

        return $$"""
                  You are helping classify a YouTube video into existing playlists.
                  Video/content context:
                  {{context.PromptEnrichment}}
                  {{latestPlaylistTitlesSection}}

                  Candidates:
                  {{playlistLines}}

                  Rules:
                  - Prefer precision over recall
                  - Select only clear, direct topic matches
                  - Do not infer weak or broad lifestyle matches
                  - If no playlist clearly fits, return an empty array

                  Required JSON shape:
                  {
                    "i": [0]
                  }
                  """;
    }
}