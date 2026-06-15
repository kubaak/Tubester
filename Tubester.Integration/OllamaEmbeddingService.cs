using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pgvector;
using Tubester.Abstractions;

namespace Tubester.Integration;

public sealed class OllamaEmbeddingService(
    HttpClient httpClient,
    IOptions<OllamaOptions> ollamaOptions,
    ILogger<OllamaEmbeddingService> logger)
    : IEmbeddingService
{
    private const string DefaultModel = "nomic-embed-text";

    public async Task<EmbeddingResult> EmbedAsync(string text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Text must not be empty.", nameof(text));
        }

        var model = string.IsNullOrWhiteSpace(ollamaOptions.Value.EmbeddingModel)
            ? DefaultModel
            : ollamaOptions.Value.EmbeddingModel;

        logger.LogDebug(
            "Generating embedding with Ollama. Model: {Model}, TextLength: {TextLength}",
            model,
            text.Length);

        var body = new
        {
            model,
            prompt = text
        };

        var stopwatch = Stopwatch.StartNew();

        HttpResponseMessage response;
        try
        {
            response = await httpClient.PostAsJsonAsync(
                "/api/embeddings",
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
                "Ollama embeddings API request failed. Model: {Model}",
                model);

            throw new InvalidOperationException("Ollama embeddings API request failed.", ex);
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
                    "Ollama embeddings API returned non-success status. StatusCode: {StatusCode}, Body: {Body}",
                    response.StatusCode,
                    error);

                throw new InvalidOperationException(
                    $"Ollama embeddings API returned status code {(int)response.StatusCode}.");
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
                    "Failed to parse Ollama embeddings response");

                throw new InvalidOperationException("Ollama returned invalid embeddings response JSON.", ex);
            }

            using (envelopeDocument)
            {
                var envelopeRoot = envelopeDocument.RootElement;

                if (!envelopeRoot.TryGetProperty("embedding", out var embeddingElement) ||
                    embeddingElement.ValueKind != JsonValueKind.Array)
                {
                    throw new InvalidOperationException(
                        "AI embeddings response did not contain a valid 'embedding' array.");
                }

                var vectorArray = embeddingElement.EnumerateArray()
                    .Select(e => e.GetSingle())
                    .ToArray();

                if (vectorArray.Length == 0)
                {
                    throw new InvalidOperationException("AI returned an empty embedding vector.");
                }

                var vector = new Vector(vectorArray);

                logger.LogDebug(
                    "Embedding generated successfully. Model: {Model}, Dimensions: {Dimensions}, DurationMs: {DurationMs}",
                    model,
                    vector.ToArray().Length,
                    stopwatch.ElapsedMilliseconds);

                return new EmbeddingResult(vector, model);
            }
        }
    }
}