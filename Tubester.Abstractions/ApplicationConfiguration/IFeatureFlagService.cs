namespace Tubester.Abstractions.ApplicationConfiguration;

/// <summary>
/// Service for checking feature flags.
/// </summary>
public interface IFeatureFlagService
{
    /// <summary>
    /// Checks if a feature flag is enabled.
    /// </summary>
    Task<bool> IsEnabledAsync(string featureKey, CancellationToken ct);
}
