using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tubester.Integration;

public sealed class AiClient(HttpClient httpClient, IOptions<AiOptions> aiOptions, ILogger<AiClient> logger) : IAiClient
{
    private readonly AiOptions _ai = aiOptions.Value;

    public async Task<(string Title, string Description, IEnumerable<string> tags)> SuggestMetadataAsync(string context,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Getting suggested metadata for Title: {Context}", context);
        var prompt = $$"""
                       You write concise SEO-friendly YouTube metadata. Return JSON only (no code fences).
                       Generate:
                       - title (≤100 chars, no ALL CAPS, punchy)
                       - description (≤5000 chars, engaging, include hashtags)
                       - tags (for better reach on YouTube search)

                       Context: {{context}}

                       Return: {"title":"...","description":"...", "tags":["...","..."]}
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
        var title = doc.RootElement.TryGetProperty("title", out var t) ? t.GetString() : null;
        var desc = doc.RootElement.TryGetProperty("description", out var d) ? d.GetString() : null;
        var tags = new List<string>();
        if (doc.RootElement.TryGetProperty("tags", out var tagsElement))
        {
            foreach (var tagElement in tagsElement.EnumerateArray())
            {
                tags.Add(tagElement.GetString());
            }
        }

        return (title, desc, tags);
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
}