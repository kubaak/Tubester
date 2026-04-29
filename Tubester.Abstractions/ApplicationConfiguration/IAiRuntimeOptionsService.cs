namespace Tubester.Abstractions.ApplicationConfiguration;

/// <summary>
/// Service for getting AI runtime options from configuration.
/// </summary>
public interface IAiRuntimeOptionsService
{
    /// <summary>
    /// Gets the current AI runtime options.
    /// </summary>
    Task<AiRuntimeOptions> GetAsync(CancellationToken ct);
}