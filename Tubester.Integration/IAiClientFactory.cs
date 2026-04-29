namespace Tubester.Integration;

/// <summary>
/// Factory for creating AI clients based on runtime configuration.
/// </summary>
public interface IAiClientFactory
{
    /// <summary>
    /// Gets an AI client for the configured provider.
    /// </summary>
    Task<IAiClient> GetClientAsync(CancellationToken ct);
}
