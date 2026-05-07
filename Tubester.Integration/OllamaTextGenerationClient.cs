using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tubester.Abstractions;
using Tubester.Abstractions.ApplicationConfiguration;

namespace Tubester.Integration;

/// <summary>
/// Ollama-specific text generation client.
/// </summary>
public sealed class OllamaTextGenerationClient(
    HttpClient httpClient,
    IOptions<OllamaOptions> ollamaOptions,
    ILogger<OllamaTextGenerationClient> logger,
    IAiRuntimeOptionsService aiRuntimeOptionsService)
    : IAiTextGenerationClient
{
    public string Provider => AiProviders.Ollama;

    public async Task<AiTextGenerationResult> GenerateTextAsync(
        AiOperation operation,
        string prompt,
        CancellationToken cancellationToken)
    {
        var runtimeOptions = await aiRuntimeOptionsService.GetAsync(operation, cancellationToken);

        var (numCtx, numPredict) = GetContextParams(operation);

        logger.LogDebug(
            "Calling Ollama API for {Operation}. Model: {Model}, Temperature: {Temperature}, NumCtx: {NumCtx}, NumPredict: {NumPredict}",
            operation,
            runtimeOptions.Model,
            runtimeOptions.Temperature,
            numCtx,
            numPredict);

        var body = new
        {
            model = runtimeOptions.Model,
            prompt,
            stream = false,
            format = "json",
            options = new
            {
                temperature = runtimeOptions.Temperature,
                num_ctx = numCtx,
                num_predict = numPredict
            }
        };

        var stopwatch = Stopwatch.StartNew();

        HttpResponseMessage response;
        try
        {
            response = await httpClient.PostAsJsonAsync(
                "/api/generate",
                body,
                TubesterJsonSerializerOptions.DefaultWrite,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Ollama API request failed for {Operation}. Model: {Model}",
                operation,
                runtimeOptions.Model);

            throw new InvalidOperationException($"Ollama API request failed for {operation}.", ex);
        }
        finally
        {
            stopwatch.Stop();
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);

                logger.LogError(
                    "Ollama API returned non-success status for {Operation}. StatusCode: {StatusCode}, Body: {Body}",
                    operation,
                    response.StatusCode,
                    error);

                throw new InvalidOperationException(
                    $"Ollama API returned status code {(int)response.StatusCode} for {operation}.");
            }

            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

            JsonDocument envelopeDocument;

            try
            {
                envelopeDocument = JsonDocument.Parse(responseText);
            }
            catch (JsonException ex)
            {
                logger.LogError(
                    ex,
                    "Failed to parse Ollama response envelope for {Operation}",
                    operation);

                throw new InvalidOperationException($"Ollama returned invalid response envelope JSON for {operation}.", ex);
            }

            using (envelopeDocument)
            {
                var envelopeRoot = envelopeDocument.RootElement;

                if (!envelopeRoot.TryGetProperty("response", out var responseElement) ||
                    responseElement.ValueKind != JsonValueKind.String)
                {
                    throw new InvalidOperationException(
                        $"AI response for {operation} did not contain a valid 'response' string.");
                }

                var content = responseElement.GetString();

                if (string.IsNullOrWhiteSpace(content))
                {
                    throw new InvalidOperationException($"AI returned an empty response payload for {operation}.");
                }

                // Extract usage metadata from Ollama response
                var promptEvalCount = TryGetInt64(envelopeRoot, "prompt_eval_count");
                var evalCount = TryGetInt64(envelopeRoot, "eval_count");
                long? totalTokens = null;
                if (promptEvalCount.HasValue && evalCount.HasValue)
                {
                    totalTokens = promptEvalCount.Value + evalCount.Value;
                }

                return new AiTextGenerationResult(
                    content,
                    new AiUsage(
                        Provider: AiProviders.Ollama,
                        Model: runtimeOptions.Model,
                        PromptTokens: (int?)promptEvalCount,
                        CompletionTokens: (int?)evalCount,
                        TotalTokens: totalTokens.HasValue ? (int?)totalTokens.Value : null,
                        MaxOutputTokens: numPredict,
                        Temperature: runtimeOptions.Temperature,
                        Duration: stopwatch.Elapsed));
            }
        }
    }

    private (int NumCtx, int NumPredict) GetContextParams(AiOperation operation)
    {
        return operation switch
        {
            AiOperation.Metadata => (ollamaOptions.Value.MetadataNumCtx, ollamaOptions.Value.MetadataNumPredict),
            AiOperation.Reply => (ollamaOptions.Value.ReplyNumCtx, ollamaOptions.Value.ReplyNumPredict),
            AiOperation.PlaylistSuggestion => (ollamaOptions.Value.PlaylistNumCtx, ollamaOptions.Value.PlaylistNumPredict),
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unknown AI operation.")
        };
    }

    private static long? TryGetInt64(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var element) &&
               element.ValueKind == JsonValueKind.Number &&
               element.TryGetInt64(out var value)
            ? value
            : null;
    }
}