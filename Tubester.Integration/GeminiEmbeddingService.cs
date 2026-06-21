using Google.GenAI.Types;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pgvector;
using Tubester.Abstractions;

namespace Tubester.Integration;

public sealed class GeminiEmbeddingService(
    IGeminiClientFactory clientFactory,
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
        var client = clientFactory.CreateClient();

        EmbedContentResponse response;

        try
        {
            response = await client.Models.EmbedContentAsync(
                model: config.EmbeddingModel,
                contents: text.Trim(),
                config: new EmbedContentConfig
                {
                    TaskType = "RETRIEVAL_DOCUMENT",
                    OutputDimensionality = config.OutputDimensionality
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
                "Gemini embedding request failed. Model: {Model}",
                config.EmbeddingModel);

            throw new InvalidOperationException("Gemini embedding request failed.", ex);
        }

        var vector = response.Embeddings?
            .FirstOrDefault()?
            .Values?
            .Select(value => (float)value)
            .ToArray();

        if (vector is null || vector.Length == 0)
        {
            throw new InvalidOperationException("Gemini embedding response did not contain a vector.");
        }

        if (vector.Length != config.OutputDimensionality)
        {
            throw new InvalidOperationException(
                $"Gemini returned {vector.Length} dimensions, but {config.OutputDimensionality} were expected.");
        }

        logger.LogDebug(
            "Gemini embedding generated. Model={Model}, Dimensions={Dimensions}",
            config.EmbeddingModel,
            vector.Length);

        return new EmbeddingResult(new Vector(vector), config.EmbeddingModel);
    }
}