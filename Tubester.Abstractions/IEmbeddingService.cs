using Pgvector;

namespace Tubester.Abstractions;

public sealed record EmbeddingResult(
    Vector Vector,
    string Model);

public interface IEmbeddingService
{
    Task<EmbeddingResult> EmbedAsync(string text, CancellationToken cancellationToken);
}
