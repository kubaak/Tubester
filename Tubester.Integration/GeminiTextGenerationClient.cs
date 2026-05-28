using System.Diagnostics;
using Google.GenAI.Types;
using Microsoft.Extensions.Logging;
using Tubester.Abstractions.ApplicationConfiguration;

namespace Tubester.Integration;

/// <summary>
/// Gemini-specific text generation client.
/// </summary>
public sealed class GeminiTextGenerationClient(
    ILogger<GeminiTextGenerationClient> logger,
    IAiRuntimeOptionsService aiRuntimeOptionsService,
    IGeminiClientFactory clientFactory)
    : IAiTextGenerationClient
{
    private const string JsonMimeType = "application/json";

    public string Provider => AiProviders.Gemini;

    public async Task<AiTextGenerationResult> GenerateTextAsync(
        AiOperation operation,
        string prompt,
        CancellationToken cancellationToken)
    {
        var settings = await aiRuntimeOptionsService.GetAsync(operation, cancellationToken);

        logger.LogDebug(
            "Calling Gemini API for {Operation}. Model: {Model}, Temperature: {Temperature}, MaxOutputTokens: {MaxOutputTokens}",
            operation,
            settings.Model,
            settings.Temperature,
            settings.MaxOutputTokens);

        var stopwatch = Stopwatch.StartNew();
        var client = clientFactory.CreateClient();

        GenerateContentResponse response;

        try
        {
            response = await client.Models.GenerateContentAsync(
                model: settings.Model,
                contents: prompt,
                config: new GenerateContentConfig
                {
                    Temperature = settings.Temperature,
                    MaxOutputTokens = settings.MaxOutputTokens,
                    ResponseMimeType = JsonMimeType,
                    ThinkingConfig = new ThinkingConfig
                    {
                        ThinkingBudget = 0 //todo load from configuration
                    }
                },
                cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Gemini API call failed for {Operation}. Model: {Model}",
                operation,
                settings.Model);

            throw new InvalidOperationException($"Gemini API call failed for {operation}.", ex);
        }
        finally
        {
            stopwatch.Stop();
        }

        var firstCandidate = response.Candidates?.FirstOrDefault();

        var responseText = firstCandidate?.Content?.Parts is { Count: > 0 } parts
            ? string.Concat(parts.Select(p => p.Text))
            : null;

        if (string.IsNullOrWhiteSpace(responseText))
        {
            var finishReason = firstCandidate?.FinishReason;

            logger.LogError(
                "Gemini returned an empty response for {Operation}. FinishReason: {FinishReason}",
                operation,
                finishReason);

            throw new InvalidOperationException($"Gemini returned an empty response for {operation}.");
        }

        return new AiTextGenerationResult(
            responseText,
            new AiUsage(
                Provider: AiProviders.Gemini,
                Model: settings.Model,
                PromptTokens: response.UsageMetadata?.PromptTokenCount,
                CompletionTokens: response.UsageMetadata?.CandidatesTokenCount,
                TotalTokens: response.UsageMetadata?.TotalTokenCount,
                MaxOutputTokens: settings.MaxOutputTokens,
                Temperature: settings.Temperature,
                Duration: stopwatch.Elapsed));
    }
}