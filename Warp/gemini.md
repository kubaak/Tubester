Implement a second production implementation of `IAiClient` for Google Gemini.

Context:
The project is Tubester. There is already an `IAiClient` abstraction and an existing AI client implementation, likely for Ollama. Implement a new Gemini implementation without changing the public behavior of the existing abstraction.

Goal:
Create a new `GeminiAiClient : IAiClient` that calls the Google Gemini API for:

- metadata suggestions
- YouTube comment reply suggestions
- playlist ID suggestions

Recommended default model:
`gemini-2.5-flash`

Provider name:
`GeminiAiClient.Provider` should return `"Gemini"`.

Configuration design:
Tubester has a dynamic application configuration system backed by a cached DB table.

The following Gemini runtime settings must come from dynamic application configuration, not from normal appsettings options:

- Model
- Temperature
- BaseUrl
- MaxOutputTokens

The Gemini API key should remain in appsettings/secrets/environment variables because it is a secret.

Create or reuse constants for dynamic configuration keys, for example:

public static class AiConfigurationKeys
{
public const string GeminiModel = "AI:Gemini:Model";
public const string GeminiTemperature = "AI:Gemini:Temperature";
public const string GeminiBaseUrl = "AI:Gemini:BaseUrl";
public const string GeminiMaxOutputTokens = "AI:Gemini:MaxOutputTokens";
}

Default runtime values if dynamic configuration values are missing:

- Model: `"gemini-2.5-flash"`
- Temperature: `0.4`
- BaseUrl: `"https://generativelanguage.googleapis.com"`
- MaxOutputTokens: `null`

The options class should only contain secret/static values:

public sealed class GeminiAiOptions
{
public string ApiKey { get; init; } = null!;
}

Inject these dependencies into `GeminiAiClient`:

- `HttpClient`
- `ILogger<GeminiAiClient>`
- `IOptions<GeminiAiOptions>`
- `IApplicationConfigurationService`

Example constructor shape:

public sealed class GeminiAiClient(
HttpClient httpClient,
ILogger<GeminiAiClient> logger,
IOptions<GeminiAiOptions> options,
IApplicationConfigurationService applicationConfigurationService)
: IAiClient
{
public string Provider => "Gemini";
}

Runtime settings helper:
Add a private helper to resolve Gemini runtime settings per request.

Example:

private async Task<GeminiRuntimeSettings> GetRuntimeSettingsAsync(CancellationToken ct)
{
var model = await applicationConfigurationService.GetValueAsync<string>(
AiConfigurationKeys.GeminiModel,
ct) ?? "gemini-2.5-flash";

    var baseUrl = await applicationConfigurationService.GetValueAsync<string>(
        AiConfigurationKeys.GeminiBaseUrl,
        ct) ?? "https://generativelanguage.googleapis.com";

    var temperature = await applicationConfigurationService.GetValueAsync<double?>(
        AiConfigurationKeys.GeminiTemperature,
        ct) ?? 0.4;

    var maxOutputTokens = await applicationConfigurationService.GetValueAsync<int?>(
        AiConfigurationKeys.GeminiMaxOutputTokens,
        ct);

    return new GeminiRuntimeSettings(
        model.Trim(),
        baseUrl.TrimEnd('/'),
        temperature,
        maxOutputTokens);
}

private sealed record GeminiRuntimeSettings(
string Model,
string BaseUrl,
double Temperature,
int? MaxOutputTokens);

If the existing `IApplicationConfigurationService.GetValueAsync<T>` does not correctly support nullable primitive types like `double?` and `int?`, do not force that change unless it fits naturally. Instead, read those values as strings and parse them defensively inside `GeminiAiClient`.

Gemini API:
Call the `generateContent` endpoint:

POST {BaseUrl}/v1beta/models/{Model}:generateContent?key={ApiKey}

Build the URL from dynamic runtime settings:

var url = $"{settings.BaseUrl}/v1beta/models/{Uri.EscapeDataString(settings.Model)}:generateContent?key={apiKey}";

Request body should follow this general shape:

{
"contents": [
{
"role": "user",
"parts": [
{
"text": "..."
}
]
}
],
"generationConfig": {
"temperature": 0.4,
"responseMimeType": "application/json"
}
}

Include `maxOutputTokens` only when the dynamic setting has a value.

The response text is usually located at:

response.candidates[0].content.parts[0].text

Implement internal DTOs for request and response. Do not leak Gemini-specific DTOs outside the integration layer.

Implementation requirements:
1. Use `HttpClient`, `ILogger<GeminiAiClient>`, `IOptions<GeminiAiOptions>`, `IApplicationConfigurationService`, and `System.Text.Json`.

2. Use cancellation tokens on all async calls.

3. Use `responseMimeType: "application/json"`.

4. Use strict prompts that say:
   `Return exactly one valid JSON object. Do not wrap it in markdown. Do not include explanations.`

5. Parse JSON safely.

6. Add a defensive helper for extracting JSON in case the model accidentally returns code fences or text around JSON. Prefer strict JSON first, but handle common failure cases gracefully.

7. Throw a meaningful custom exception if the project already has one for AI client failures. Otherwise throw `InvalidOperationException` with a useful message when:
    - Gemini returns a non-success HTTP response
    - the response has no candidates
    - the response text is empty
    - the response JSON cannot be parsed
    - the parsed JSON does not match the expected shape

8. Log request intent and response failures, but do not log:
    - API keys
    - full prompts containing user/video/comment data unless the existing project already logs those intentionally

9. Keep the public behavior compatible with the existing `IAiClient`.

Internal helper:
Add a helper similar to:

private async Task<T> GenerateJsonAsync<T>(
string prompt,
CancellationToken cancellationToken)

This method should:
- resolve runtime settings
- validate the API key
- build the Gemini request
- send the HTTP request
- ensure success status
- extract the first candidate text
- extract/normalize JSON if needed
- deserialize into `T`
- throw meaningful exceptions on failure

Internal DTO suggestions:
Add private/internal DTOs such as:

- `GeminiGenerateContentRequest`
- `GeminiContent`
- `GeminiPart`
- `GeminiGenerationConfig`
- `GeminiGenerateContentResponse`
- `GeminiCandidate`

Add small private result DTOs such as:

- `GeminiMetadataResult`
- `GeminiReplyResult`
- `GeminiPlaylistResult`

Use `JsonSerializerOptions` with:
- camelCase property naming for writing
- case-insensitive property reading
- ignore null values when writing if appropriate

Method behavior:

1. `SuggestMetadataAsync`

Prompt requirements:
- Generate only the requested fields.
- If a field is not requested, return null or empty array according to the existing C# model.
- Title should be optimized for YouTube.
- Description should be useful, natural, and YouTube-friendly.
- Tags should be relevant YouTube tags.
- Do not invent facts not present in the context.
- Return JSON only.

Expected JSON shape:

{
"title": "string or null",
"description": "string or null",
"tags": ["tag1", "tag2"]
}

Rules:
- If title generation is disabled, `title` must be null.
- If description generation is disabled, `description` must be null.
- If tag generation is disabled, `tags` must be [].
- Tags should not contain hashtags.
- Tags should be concise.
- Remove duplicate tags.
- Do not exceed roughly 15 tags.

After parsing:
- Enforce disabled flags defensively in C# too.
- Normalize tags by trimming, removing empty values, removing leading `#`, removing duplicates, and limiting to 15.

Suggested prompt:

You are helping a YouTube creator optimize video metadata.

Return exactly one valid JSON object.
Do not wrap the response in markdown.
Do not include explanations.
Do not include any keys other than: title, description, tags.

Generation flags:
- generateTitle: {generateTitle}
- generateDescription: {generateDescription}
- generateTags: {generateTags}

Rules:
- If generateTitle is false, return "title": null.
- If generateDescription is false, return "description": null.
- If generateTags is false, return "tags": [].
- Do not invent facts that are not supported by the context.
- The title should be compelling but not clickbait.
- The description should be natural, useful, and YouTube-friendly.
- Tags should be relevant YouTube search tags.
- Tags must not contain hashtags.
- Return at most 15 tags.
- Remove duplicate tags.

Context:
{context}

Required JSON shape:
{
"title": "string or null",
"description": "string or null",
"tags": ["string"]
}

2. `SuggestReplyAsync`

Prompt requirements:
- Suggest a short, friendly YouTube comment reply.
- Use the requested language.
- Keep it natural and human.
- Do not be generic if the comment has specific content.
- If the comment is spam, hateful, meaningless, emoji-only, or cannot be answered usefully, return null.
- Do not claim things that are not known.
- Return JSON only.

Expected JSON shape:

{
"reply": "string or null"
}

Rules:
- Reply should usually be one sentence.
- Avoid sounding like a bot.
- No markdown.
- If language is empty or unknown, use English.

After parsing:
- Trim reply.
- Return null if reply is null, empty, or whitespace.

Suggested prompt:

You are helping a YouTube creator reply to a viewer comment.

Return exactly one valid JSON object.
Do not wrap the response in markdown.
Do not include explanations.
Do not include any keys other than: reply.

Video title:
{videoTitle}

Video tags:
{tags}

Viewer comment:
{commentText}

Target reply language:
{language}

Rules:
- Write a short, friendly, natural reply.
- Usually write one sentence.
- Use the target language. If the language is empty or unknown, use English.
- Do not sound corporate or robotic.
- Do not claim facts that are not known.
- If the comment is spam, hateful, meaningless, emoji-only, or not useful to answer, return null.

Required JSON shape:
{
"reply": "string or null"
}

3. `SuggestPlaylistIdsAsync`

Prompt requirements:
- Classify which playlists fit the current video/context.
- Only return playlist IDs from the provided candidate list.
- Never invent playlist IDs.
- Use `PromptEnrichment` as the video/content context.
- Use `LatestPlaylistTitlesUsed` as a weak hint about recently used playlist patterns.
- Return JSON only.

Expected JSON shape:

{
"playlistIds": ["playlist-id-1", "playlist-id-2"]
}

Rules:
- `playlistIds` must only contain IDs that exist in the candidate playlist input.
- If no playlist is clearly suitable, return an empty array.
- Prefer precision over recall.
- Remove duplicates.

After parsing:
- Filter out any playlist IDs that were not in the provided candidates.
- Remove duplicates.
- Preserve the model’s returned order for valid IDs.

Suggested prompt:

You are helping classify a YouTube video into existing playlists.

Return exactly one valid JSON object.
Do not wrap the response in markdown.
Do not include explanations.
Do not include any keys other than: playlistIds.

Video/content context:
{context.PromptEnrichment}

Recently used playlist titles:
{context.LatestPlaylistTitlesUsed}

Candidate playlists:
{playlists as JSON array with playlistId and name}

Rules:
- Only return playlist IDs from the provided candidate playlists.
- Never invent playlist IDs.
- Prefer precision over recall.
- If no playlist clearly fits, return an empty array.
- Remove duplicates.
- Use recently used playlist titles only as a weak hint.

Required JSON shape:
{
"playlistIds": ["string"]
}

DI registration:
Add DI registration for Gemini.

Example:

services.Configure<GeminiAiOptions>(
configuration.GetSection("AI:Gemini"));

services.AddHttpClient<GeminiAiClient>();

Then integrate Gemini into the existing provider selection mechanism.

Example:

services.AddScoped<IAiClient>(sp =>
{
var options = sp.GetRequiredService<IOptions<AiOptions>>().Value;

    return options.Provider switch
    {
        "Gemini" => sp.GetRequiredService<GeminiAiClient>(),
        "Ollama" => sp.GetRequiredService<OllamaAiClient>(),
        _ => throw new InvalidOperationException($"Unsupported AI provider: {options.Provider}")
    };
});

If the project already has a provider selection mechanism, reuse it instead of creating a duplicate one.

Appsettings example:
Only put the secret/static Gemini config in appsettings or environment variables:

{
"AI": {
"Provider": "Gemini",
"Gemini": {
"ApiKey": "<from secret/env>"
}
}
}

Dynamic DB seed/defaults:
Add or update seed/default configuration values for:

new ApplicationConfiguration
{
Key = "AI:Gemini:Model",
Value = "gemini-2.5-flash"
},
new ApplicationConfiguration
{
Key = "AI:Gemini:BaseUrl",
Value = "https://generativelanguage.googleapis.com"
},
new ApplicationConfiguration
{
Key = "AI:Gemini:Temperature",
Value = "0.4"
},
new ApplicationConfiguration
{
Key = "AI:Gemini:MaxOutputTokens",
Value = null
}

Testing:
Add unit tests without calling the real Gemini API.

Use a fake `HttpMessageHandler`, mocked HTTP pipeline, or the project’s existing HTTP testing style.

Tests to add:
1. `Provider` returns `"Gemini"`.
2. `SuggestMetadataAsync` parses a valid Gemini JSON response.
3. `SuggestMetadataAsync` defensively respects disabled flags even if the model returns values.
4. `SuggestMetadataAsync` normalizes tags.
5. `SuggestReplyAsync` returns a string when reply is present.
6. `SuggestReplyAsync` returns null when reply is null or whitespace.
7. `SuggestPlaylistIdsAsync` filters out invented playlist IDs defensively.
8. Invalid JSON response throws a meaningful exception.
9. Non-success HTTP response throws a meaningful exception.
10. Runtime settings are read from `IApplicationConfigurationService`.
11. Missing runtime settings use defaults.

Deliverables:
- `GeminiAiOptions`
- `GeminiAiClient`
- internal Gemini request/response DTOs
- result DTOs for metadata/reply/playlists
- dynamic config key constants
- DI registration changes
- appsettings example
- dynamic configuration seed/defaults if the project has seeding for application configuration
- unit tests