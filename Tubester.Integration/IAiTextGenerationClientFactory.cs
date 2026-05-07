namespace Tubester.Integration;

/// <summary>
/// Factory for creating AI clients based on runtime configuration.
/// </summary>
public interface IAiTextGenerationClientFactory
{
    /// <summary>
    /// Gets an AI text generation client for the configured provider.
    /// </summary>
    Task<IAiTextGenerationClient> GetClientAsync(CancellationToken ct);
}
