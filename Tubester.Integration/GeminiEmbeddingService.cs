using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pgvector;
using Tubester.Abstractions;

namespace Tubester.Integration;

public sealed class GeminiEmbeddingService(
    HttpClient httpClient,
    IOptions<GeminiOptions> options,
    ILogger<GeminiEmbeddingService> logger)
    : IEmbeddingService
{
    public async Task<EmbeddingResult> EmbedAsync(
        string text,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Text for embedding cannot be empty.", nameof(text));
        }

        var config = options.Value;

        if (string.IsNullOrWhiteSpace(config.ApiKey))
        {
            throw new InvalidOperationException("Gemini embedding API key is missing.");
        }

        var request = new GeminiEmbedContentRequest
        {
            Model = $"models/{config.Model}",
            Content = new GeminiContent
            {
                Parts =
                [
                    new GeminiPart
                    {
                        Text = text.Trim()
                    }
                ]
            },
            TaskType = "RETRIEVAL_DOCUMENT",
            OutputDimensionality = config.OutputDimensionality
        };

        var url =
            $"https://generativelanguage.googleapis.com/v1beta/models/{config.Model}:embedContent?key={config.ApiKey}";

        using var response = await httpClient.PostAsJsonAsync(
            url,
            request,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);

            logger.LogWarning(
                "Gemini embedding request failed. StatusCode: {StatusCode}. Body: {Body}",
                response.StatusCode,
                error);

            throw new InvalidOperationException(
                $"Gemini embedding request failed with status code {response.StatusCode}.");
        }

        var result = await response.Content.ReadFromJsonAsync<GeminiEmbedContentResponse>(
            cancellationToken);

        var vector = result?.Embedding?.Values;

        if (vector is null || vector.Length == 0)
        {
            throw new InvalidOperationException("Gemini embedding response did not contain a vector.");
        }

        if (vector.Length != config.OutputDimensionality)
        {
            throw new InvalidOperationException(
                $"Gemini returned {vector.Length} dimensions, but {config.OutputDimensionality} were expected.");
        }

        return new EmbeddingResult(new Vector(vector), config.Model);
    }

    private sealed class GeminiEmbedContentRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; init; } = string.Empty;

        [JsonPropertyName("content")]
        public GeminiContent Content { get; init; } = new();

        [JsonPropertyName("taskType")]
        public string TaskType { get; init; } = "RETRIEVAL_DOCUMENT";

        [JsonPropertyName("outputDimensionality")]
        public int OutputDimensionality { get; init; }
    }

    private sealed class GeminiContent
    {
        [JsonPropertyName("parts")]
        public GeminiPart[] Parts { get; init; } = [];
    }

    private sealed class GeminiPart
    {
        [JsonPropertyName("text")]
        public string Text { get; init; } = string.Empty;
    }

    private sealed class GeminiEmbedContentResponse
    {
        [JsonPropertyName("embedding")]
        public GeminiEmbedding? Embedding { get; init; }
    }

    private sealed class GeminiEmbedding
    {
        [JsonPropertyName("values")]
        public float[] Values { get; init; } = [];
    }
}