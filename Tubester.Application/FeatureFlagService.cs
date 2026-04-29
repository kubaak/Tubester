using Tubester.Abstractions.ApplicationConfiguration;

namespace Tubester.Application;

public sealed class FeatureFlagService(IApplicationConfigurationService configService) : IFeatureFlagService
{
    private const string FeaturePrefix = "Feature.";

    public async Task<bool> IsEnabledAsync(string featureKey, CancellationToken ct)
    {
        var key = featureKey.StartsWith(FeaturePrefix, StringComparison.Ordinal)
            ? featureKey
            : FeaturePrefix + featureKey;

        var value = await configService.GetValueAsync<string>(key, ct);

        if (value is null)
        {
            return false;
        }

        return bool.TryParse(value, out var result) && result;
    }
}
