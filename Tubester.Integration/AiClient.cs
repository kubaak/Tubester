using Microsoft.Extensions.Logging;
using Tubester.Abstractions.ApplicationConfiguration;
using Tubester.Abstractions.Playlists;

namespace Tubester.Integration;

/// <summary>
/// Provider-agnostic AI client that orchestrates text generation workflows.
/// </summary>
public sealed partial class AiClient(
    IAiTextGenerationClientFactory textGenerationClientFactory,
    IAiPromptBuilder promptBuilder,
    ILogger<AiClient> logger)
    : IAiClient
{
    public async Task<SuggestedMetadata> SuggestMetadataAsync(
        string context,
        bool generateTitle,
        bool generateDescription,
        bool generateTags,
        CancellationToken cancellationToken)
    {
        var aiTextGenerationClient = await textGenerationClientFactory.GetClientAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(context))
        {
            throw new ArgumentException("Context must not be empty.", nameof(context));
        }

        if (!generateTitle && !generateDescription && !generateTags)
        {
            throw new ArgumentException("At least one metadata field must be requested.");
        }

        logger.LogInformation(
            "Getting suggested metadata from {Provider}. GenerateTitle: {GenerateTitle}, GenerateDescription: {GenerateDescription}, GenerateTags: {GenerateTags}",
            aiTextGenerationClient.Provider,
            generateTitle,
            generateDescription,
            generateTags);

        var prompt = promptBuilder.BuildMetadataPrompt(context, generateTitle, generateDescription, generateTags);

        var result = await aiTextGenerationClient.GenerateTextAsync(
            AiOperation.Metadata,
            prompt,
            cancellationToken);

        LogUsage(result.Usage, AiOperation.Metadata);

        var parseResult = AiJsonResponseParser.DeserializeModelJson<AiMetadataJsonResult>(
            result.Text,
            logger);

        return AiSuggestionNormalizer.ToSuggestedMetadata(
            parseResult,
            generateTitle,
            generateDescription,
            generateTags);
    }

    public async Task<string?> SuggestReplyAsync(
        string videoTitle,
        IEnumerable<string> tags,
        string commentText,
        string language,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(commentText))
        {
            return null;
        }

        var tagList = tags.ToList();

        var aiTextGenerationClient = await textGenerationClientFactory.GetClientAsync(cancellationToken);
        logger.LogInformation(
            "Getting suggested reply from {Provider}. TagCount: {TagCount}, Language: {Language}, CommentLength: {CommentLength}",
            aiTextGenerationClient.Provider,
            tagList.Count,
            language,
            commentText.Length);

        var prompt = promptBuilder.BuildReplyPrompt(videoTitle, tagList, commentText, language);

        var result = await aiTextGenerationClient.GenerateTextAsync(
            AiOperation.Reply,
            prompt,
            cancellationToken);

        LogUsage(result.Usage, AiOperation.Reply);

        var parseResult = AiJsonResponseParser.DeserializeModelJson<AiReplyJsonResult>(
            result.Text,
            logger);

        return AiSuggestionNormalizer.NormalizeReply(parseResult.Reply);
    }

    public async Task<IEnumerable<string>> SuggestPlaylistIdsAsync(
        PlaylistSuggestionContext context,
        IReadOnlyList<PlaylistCandidateDto> playlists,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(playlists);

        if (playlists.Count == 0)
        {
            return [];
        }
        var aiTextGenerationClient = await textGenerationClientFactory.GetClientAsync(cancellationToken);
        logger.LogInformation(
            "Getting suggested playlist ids from {Provider}. PlaylistCount: {PlaylistCount}, PromptLength: {PromptLength}, LatestPlaylistTitleCount: {LatestPlaylistTitleCount}",
            aiTextGenerationClient.Provider,
            playlists.Count,
            context.PromptEnrichment?.Length ?? 0,
            context.LatestPlaylistTitlesUsed?.Count ?? 0);

        var prompt = promptBuilder.BuildPlaylistPrompt(context, playlists);

        var result = await aiTextGenerationClient.GenerateTextAsync(
            AiOperation.PlaylistSuggestion,
            prompt,
            cancellationToken);
        
        logger.LogDebug("Playlist suggestion result: {Text}", result.Text);

        LogUsage(result.Usage, AiOperation.PlaylistSuggestion);

        var parseResult = AiJsonResponseParser.DeserializeModelJson<AiPlaylistJsonResult>(
            result.Text,
            logger);

        return AiSuggestionNormalizer.MapPlaylistIndexesToIds(
            parseResult.I,
            playlists);
    }

    private void LogUsage(AiUsage usage, AiOperation operation)
    {
        LogAiUsageProviderProviderModelModelOperationOperationPromptTokens(usage.Provider, usage.Model, operation, usage.PromptTokens, usage.CompletionTokens, usage.TotalTokens, usage.MaxOutputTokens, usage.Temperature, usage.Duration.TotalMilliseconds);
    }

    [LoggerMessage(LogLevel.Information, "AI usage. Provider: {Provider}, Model: {Model}, Operation: {Operation}, PromptTokens: {PromptTokens}, CompletionTokens: {CompletionTokens}, TotalTokens: {TotalTokens}, MaxOutputTokens: {MaxOutputTokens}, Temperature: {Temperature}, DurationMs: {DurationMs}")]
    partial void LogAiUsageProviderProviderModelModelOperationOperationPromptTokens(string provider, string model, AiOperation operation, int? promptTokens, int? completionTokens, int? totalTokens, int? maxOutputTokens, double? temperature, double durationMs);
}