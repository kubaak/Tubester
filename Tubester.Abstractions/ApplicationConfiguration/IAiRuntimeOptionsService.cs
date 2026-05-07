namespace Tubester.Abstractions.ApplicationConfiguration;

/// <summary>
/// Service for getting AI runtime options from configuration.
/// </summary>
public interface IAiRuntimeOptionsService
{
    /// <summary>
    /// Gets the current AI runtime options.
    /// </summary>
    Task<AiRuntimeOptions> GetAsync(AiOperation operation, CancellationToken ct);
    /// <summary>
    /// Gets the current AI provider.
    /// </summary>
    Task<string> GetProviderAsync(CancellationToken ct);
}

/// <summary>
/// Represents the type of AI operation being performed.
/// </summary>
public enum AiOperation
{
    /// <summary>
    /// Metadata generation operation.
    /// </summary>
    Metadata,

    /// <summary>
    /// Reply suggestion operation.
    /// </summary>
    Reply,

    /// <summary>
    /// Playlist suggestion operation.
    /// </summary>
    PlaylistSuggestion
}