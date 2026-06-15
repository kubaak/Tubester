namespace Tubester.Abstractions;

/// <summary>
/// Factory for creating embedding service clients based on runtime configuration.
/// </summary>
public interface IEmbeddingServiceFactory
{
    /// <summary>
    /// Gets an embedding service for the configured provider.
    /// </summary>
    Task<IEmbeddingService> GetServiceAsync(CancellationToken ct);
}
