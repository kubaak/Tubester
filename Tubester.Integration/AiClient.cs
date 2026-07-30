using Microsoft.Extensions.Logging;
using Tubester.Abstractions;
using Tubester.Abstractions.ApplicationConfiguration;
using Tubester.Abstractions.Observability;
using Tubester.Abstractions.Playlists;

namespace Tubester.Integration;

/// <summary>
/// Provider-agnostic AI client that orchestrates text generation workflows.
/// </summary>
public sealed partial class AiClient(
    IAiTextGenerationClientFactory textGenerationClientFactory,
    IAiPromptBuilder promptBuilder,
    IAiJsonResponseParser jsonResponseParser,
    ILogger<AiClient> logger,
    ITubesterMetrics metrics)
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

        try
        {
            var result = await aiTextGenerationClient.GenerateTextAsync(
                AiOperation.Metadata,
                prompt,
                cancellationToken);

            metrics.AiCall(nameof(AiOperation.Metadata));
            LogUsage(result.Usage, AiOperation.Metadata);

            var parseResult = jsonResponseParser.DeserializeModelResponse<AiMetadataJsonResult>(
                result.Text);

            return AiSuggestionNormalizer.ToSuggestedMetadata(
                parseResult,
                generateTitle,
                generateDescription,
                generateTags);
        }
        catch (Exception ex)
        {
            metrics.AiCallFailed(nameof(AiOperation.Metadata));
            logger.LogError(ex, "AI metadata suggestion failed from {Provider}", aiTextGenerationClient.Provider);
            throw;
        }
    }

    public async Task<string?> SuggestReplyAsync(
        string videoTitle,
        string commentText,
        string language,
        IReadOnlyList<RelevantReplyExample>? relevantExamples,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(commentText))
        {
            return null;
        }

        var aiTextGenerationClient = await textGenerationClientFactory.GetClientAsync(cancellationToken);
        logger.LogInformation(
            "Getting suggested reply from {Provider}.Language: {Language}, CommentLength: {CommentLength}, RelevantExamples: {ExampleCount}",
            aiTextGenerationClient.Provider,
            language,
            commentText.Length,
            relevantExamples?.Count ?? 0);

        var prompt = promptBuilder.BuildReplyPrompt(videoTitle, commentText, language, relevantExamples);

        try
        {
            var result = await aiTextGenerationClient.GenerateTextAsync(
                AiOperation.Reply,
                prompt,
                cancellationToken);

            metrics.AiCall(nameof(AiOperation.Reply));
            LogUsage(result.Usage, AiOperation.Reply);

            var parseResult = jsonResponseParser.DeserializeModelResponse<AiReplyJsonResult>(
                result.Text);

            return AiSuggestionNormalizer.NormalizeReply(parseResult.Reply);
        }
        catch (Exception ex)
        {
            metrics.AiCallFailed(nameof(AiOperation.Reply));
            logger.LogError(ex, "AI reply suggestion failed from {Provider}", aiTextGenerationClient.Provider);
            throw;
        }
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

        try
        {
            var result = await aiTextGenerationClient.GenerateTextAsync(
                AiOperation.PlaylistSuggestion,
                prompt,
                cancellationToken);

            metrics.AiCall(nameof(AiOperation.PlaylistSuggestion));
            logger.LogDebug("Playlist suggestion result: {Text}", result.Text);

            LogUsage(result.Usage, AiOperation.PlaylistSuggestion);

            var parseResult = jsonResponseParser.DeserializeModelResponse<AiPlaylistJsonResult>(
                result.Text);

            return AiSuggestionNormalizer.MapPlaylistIndexesToIds(
                parseResult.I,
                playlists);
        }
        catch (Exception ex)
        {
            metrics.AiCallFailed(nameof(AiOperation.PlaylistSuggestion));
            logger.LogError(ex, "AI playlist suggestion failed from {Provider}", aiTextGenerationClient.Provider);
            throw;
        }
    }

    private void LogUsage(AiUsage usage, AiOperation operation)
    {
        LogAiUsageProviderProviderModelModelOperationOperationPromptTokens(usage.Provider, usage.Model, operation, usage.PromptTokens, usage.CompletionTokens, usage.TotalTokens, usage.MaxOutputTokens, usage.Temperature, usage.Duration.TotalMilliseconds);
    }

    [LoggerMessage(LogLevel.Information, "AI usage. Provider: {Provider}, Model: {Model}, Operation: {Operation}, PromptTokens: {PromptTokens}, CompletionTokens: {CompletionTokens}, TotalTokens: {TotalTokens}, MaxOutputTokens: {MaxOutputTokens}, Temperature: {Temperature}, DurationMs: {DurationMs}")]
    partial void LogAiUsageProviderProviderModelModelOperationOperationPromptTokens(string provider, string model, AiOperation operation, int? promptTokens, int? completionTokens, int? totalTokens, int? maxOutputTokens, double? temperature, double durationMs);
}
