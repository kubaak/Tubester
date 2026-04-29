using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tubester.Abstractions.ApplicationConfiguration;
using Tubester.Abstractions.Playlists;

namespace Tubester.Integration;

public sealed class AiClient(
    HttpClient httpClient, 
    IOptions<AiOptions> aiOptions, 
    ILogger<AiClient> logger)
    : IAiClient
{
    private readonly AiOptions _ai = aiOptions.Value;

    /// <inheritdoc />
    public string Provider => AiProviders.Ollama;

    public async Task<SuggestedMetadata> SuggestMetadataAsync(
        string context,
        bool generateTitle,
        bool generateDescription,
        bool generateTags,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context))
        {
            throw new ArgumentException("Context must not be empty.", nameof(context));
        }

        if (!generateTitle && !generateDescription && !generateTags)
        {
            throw new ArgumentException("At least one metadata field must be requested.");
        }

        logger.LogInformation(
            "Getting suggested metadata. GenerateTitle: {GenerateTitle}, GenerateDescription: {GenerateDescription}, GenerateTags: {GenerateTags}, Context: {Context}",
            generateTitle,
            generateDescription,
            generateTags,
            context);

        var prompt = BuildPrompt(context, generateTitle, generateDescription, generateTags);
        logger.LogDebug("Prompt: {Prompt}", prompt);

        var body = new
        {
            model = _ai.Model,
            prompt,
            stream = false,
            format = "json",
            options = new
            {
                temperature = 0.7,
                num_ctx = 4096
            }
        };

        using var response = await httpClient.PostAsJsonAsync(
            "/api/generate",
            body,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

        using var envelopeDocument = JsonDocument.Parse(responseText);
        var envelopeRoot = envelopeDocument.RootElement;

        if (!envelopeRoot.TryGetProperty("response", out var responseElement) ||
            responseElement.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException("AI response did not contain a valid 'response' string.");
        }

        var content = responseElement.GetString();
        if (string.IsNullOrWhiteSpace(content))
        {
            logger.LogWarning("AI returned an empty response payload");
            return new SuggestedMetadata();
        }

        try
        {
            using var contentDocument = JsonDocument.Parse(content);
            return ParseSuggestedMetadata(
                contentDocument.RootElement,
                generateTitle,
                generateDescription,
                generateTags);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to parse AI JSON content. Raw content: {Content}", content);
            throw new InvalidOperationException("AI returned invalid JSON content.", ex);
        }
    }
    
    public async Task<IEnumerable<string>> SuggestPlaylistIdsAsync(
        PlaylistSuggestionContext context,
        IReadOnlyList<PlaylistCandidateDto> playlists,
        CancellationToken cancellationToken)
    {
        if (playlists is null)
        {
            throw new ArgumentNullException(nameof(playlists));
        }

        if (playlists.Count == 0)
        {
            return [];
        }

        logger.LogInformation(
            "Getting suggested playlist ids for prompt {Prompt}, {PlaylistCount} playlists",
            context.PromptEnrichment,
            playlists.Count);

        var prompt = BuildPlaylistPrompt(context, playlists);
        logger.LogDebug("Playlist suggestion prompt: {Prompt}", prompt);

        var model = string.IsNullOrWhiteSpace(_ai.PlaylistModel) ? _ai.Model : _ai.PlaylistModel;
        var temperature = _ai.PlaylistTemperature;
        var numCtx = _ai.PlaylistNumCtx;

        logger.LogDebug(
            "Using model: {Model}, temperature: {Temperature}, num_ctx: {NumCtx}",
            model,
            temperature,
            numCtx);

        var body = new
        {
            model,
            prompt,
            stream = false,
            format = "json",
            options = new
            {
                temperature,
                num_ctx = numCtx
            }
        };

        using var response = await httpClient.PostAsJsonAsync(
            "/api/generate",
            body,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

        using var envelopeDocument = JsonDocument.Parse(responseText);
        var envelopeRoot = envelopeDocument.RootElement;

        if (!envelopeRoot.TryGetProperty("response", out var responseElement) ||
            responseElement.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException("AI response did not contain a valid 'response' string.");
        }

        var content = responseElement.GetString();
        if (string.IsNullOrWhiteSpace(content))
        {
            logger.LogWarning("AI returned an empty playlist suggestion payload");
            return [];
        }

        try
        {
            using var contentDocument = JsonDocument.Parse(content);
            return ParseSuggestedPlaylistIds(contentDocument.RootElement, playlists);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to parse AI playlist suggestion JSON. Raw content: {Content}", content);
            throw new InvalidOperationException("AI returned invalid JSON content.", ex);
        }
    }

    private static SuggestedMetadata ParseSuggestedMetadata(
        JsonElement root,
        bool generateTitle,
        bool generateDescription,
        bool generateTags)
    {
        string? title = null;
        string? description = null;
        List<string> tags = [];

        if (generateTitle &&
            root.TryGetProperty("title", out var titleElement) &&
            titleElement.ValueKind == JsonValueKind.String)
        {
            title = NormalizeTitle(titleElement.GetString());
        }

        if (generateDescription &&
            root.TryGetProperty("description", out var descriptionElement) &&
            descriptionElement.ValueKind == JsonValueKind.String)
        {
            description = NormalizeDescription(descriptionElement.GetString());
        }

        if (generateTags &&
            root.TryGetProperty("tags", out var tagsElement))
        {
            tags = ParseTags(tagsElement);
        }

        return new SuggestedMetadata
        {
            Title = title,
            Description = description,
            Tags = tags
        };
    }

    private static List<string> ParseTags(JsonElement tagsElement)
    {
        List<string> tags = [];

        if (tagsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var tagElement in tagsElement.EnumerateArray())
            {
                if (tagElement.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var tag = NormalizeTag(tagElement.GetString());
                if (string.IsNullOrWhiteSpace(tag))
                {
                    continue;
                }

                if (tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                tags.Add(tag);
            }

            return tags;
        }

        if (tagsElement.ValueKind == JsonValueKind.String)
        {
            var raw = tagsElement.GetString();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return tags;
            }

            foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var tag = NormalizeTag(part);
                if (string.IsNullOrWhiteSpace(tag))
                {
                    continue;
                }

                if (tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                tags.Add(tag);
            }
        }

        return tags;
    }

    private static string BuildPrompt(
    string context,
    bool generateTitle,
    bool generateDescription,
    bool generateTags)
    {
        List<string> requestedFields = [];
        List<string> schemaParts = [];
        List<string> sectionRules = [];

        if (generateTitle)
        {
            requestedFields.Add("- title (string, max 100 characters, punchy, no ALL CAPS)");
            schemaParts.Add("""
                            "title":"..."
                            """);
            sectionRules.Add(
                """
                Title rules:
                - Make it concise and clickable
                - Keep it under 100 characters
                - Avoid ALL CAPS
                """);
        }

        if (generateDescription)
        {
            requestedFields.Add("- description (string, max 5000 characters, engaging, include relevant hashtags)");
            schemaParts.Add("""
                            "description":"..."
                            """);
            sectionRules.Add(
                """
                Description rules:
                - Make it engaging and natural
                - Include relevant hashtags
                - Keep it under 5000 characters
                """);
        }

        if (generateTags)
        {
            requestedFields.Add("- tags (array of strings, useful for YouTube search discoverability)");
            schemaParts.Add("""
                            "tags":["...","..."]
                            """);
            sectionRules.Add(
                """
                Tags rules:
                - Return an array of strings
                - Use relevant search phrases
                - Do not repeat tags
                """);
        }

        var requestedFieldsText = string.Join(Environment.NewLine, requestedFields);
        var schemaText = "{ " + string.Join(", ", schemaParts) + " }";
        var sectionRulesText = sectionRules.Count == 0
            ? string.Empty
            : string.Join(Environment.NewLine + Environment.NewLine, sectionRules) + Environment.NewLine + Environment.NewLine;

        return $"""
                 You write concise SEO-friendly YouTube metadata.
                 Return JSON only.

                 Generate only these requested fields:
                 {requestedFieldsText}

                 Rules:
                 - Return exactly one valid JSON object
                 - Include only the requested properties
                 - Do not include any extra properties
                 - Do not wrap the JSON in markdown or code fences
                 - Follow the requested constraints for each field

                 {sectionRulesText}
                 Context:
                 {context}

                 Return: {schemaText}
                 """;
    }

    private static string? NormalizeTitle(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();

        if (normalized.Length > 100)
        {
            normalized = normalized[..100].Trim();
        }

        return normalized;
    }

    private static string? NormalizeDescription(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();

        if (normalized.Length > 5000)
        {
            normalized = normalized[..5000].Trim();
        }

        return normalized;
    }

    private static string? NormalizeTag(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    public async Task<string?> SuggestReplyAsync(string videoTitle, IEnumerable<string> tags, string commentText,
        string language, CancellationToken cancellationToken)
    {
        logger.LogInformation("Getting suggested reply for Title: {Title}, Comment: {Comment}", videoTitle,
            commentText);
        var prompt = $$"""

                       System: You are the channel owner. Be brief, kind, and helpful. Return JSON only.
                       User:
                       Write a reply ≤ 2 sentences in {{language}}. If hostile, defuse politely.
                       If unsure about a fact, say you're not sure.
                       If this comment has no letters or digits (emoji-only), a short emoji reply is OK.
                       Video title: "{{videoTitle}}"
                       Tags: {{string.Join(", ", tags)}}
                       Comment: "{{commentText}}"
                       Return: {"reply":"..."}
                       """;

        var body = new
        {
            model = _ai.Model,
            prompt,
            stream = false,
            format = "json",
            options = new { temperature = 0.7, num_ctx = 4096 }
        };
        var res = await httpClient.PostAsJsonAsync("/api/generate", body, cancellationToken);
        res.EnsureSuccessStatusCode();
        using var env = JsonDocument.Parse(await res.Content.ReadAsStringAsync(cancellationToken));
        var content = env.RootElement.GetProperty("response").GetString() ?? "{}";
        using var doc = JsonDocument.Parse(content);
        return doc.RootElement.TryGetProperty("reply", out var r) ? r.GetString() : null;
    }

    private static string BuildPlaylistPrompt(
        PlaylistSuggestionContext context,
        IReadOnlyList<PlaylistCandidateDto> playlists)
    {
        var prompt = new StringBuilder();

        prompt.AppendLine("You choose which existing YouTube playlists are a good match for a video.");
        prompt.AppendLine("Return JSON only.");
        prompt.AppendLine();
        prompt.AppendLine("Rules:");
        prompt.AppendLine("- Choose only from the provided playlists");
        prompt.AppendLine("- Return only playlistIds");
        prompt.AppendLine("- Do not invent playlist ids");
        prompt.AppendLine("- Select only strong matches");
        prompt.AppendLine("- If none match, return an empty array");
        prompt.AppendLine("- Return exactly one valid JSON object");
        prompt.AppendLine("- Do not wrap the JSON in markdown or code fences");
        prompt.AppendLine();
        prompt.AppendLine("Video context:");
        prompt.AppendLine(context.PromptEnrichment);
        prompt.AppendLine();

        if (context.LatestPlaylistTitlesUsed.Count > 0)
        {
            prompt.AppendLine($"Latest video was assigned in these playlists: {string.Join(", ", context.LatestPlaylistTitlesUsed)}");
            prompt.AppendLine();
        }

        prompt.AppendLine("Available playlists:");

        foreach (var playlist in playlists)
        {
            prompt.Append("- ");
            prompt.Append(JsonSerializer.Serialize(new
            {
                playlistId = playlist.PlaylistId,
                name = playlist.Name
            }));
            prompt.AppendLine();
        }

        prompt.AppendLine();
        prompt.AppendLine("""Return: {"playlistIds":["...","..."]}""");

        return prompt.ToString();
    }

    private static IReadOnlyList<string> ParseSuggestedPlaylistIds(
        JsonElement root,
        IReadOnlyList<PlaylistCandidateDto> playlists)
    {
        if (!root.TryGetProperty("playlistIds", out var playlistIdsElement))
        {
            return [];
        }

        var allowedIds = playlists
            .Select(x => x.PlaylistId)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.Ordinal);

        List<string> result = [];

        if (playlistIdsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var idElement in playlistIdsElement.EnumerateArray())
            {
                if (idElement.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var id = idElement.GetString()?.Trim();
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                if (!allowedIds.Contains(id))
                {
                    continue;
                }

                if (result.Contains(id, StringComparer.Ordinal))
                {
                    continue;
                }

                result.Add(id);
            }

            return result;
        }

        if (playlistIdsElement.ValueKind == JsonValueKind.String)
        {
            var raw = playlistIdsElement.GetString();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return [];
            }

            foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var id = part.Trim();
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                if (!allowedIds.Contains(id))
                {
                    continue;
                }

                if (result.Contains(id, StringComparer.Ordinal))
                {
                    continue;
                }

                result.Add(id);
            }
        }

        return result;
    }
}