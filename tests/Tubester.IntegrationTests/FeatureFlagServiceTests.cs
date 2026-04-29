using Microsoft.Extensions.DependencyInjection;
using Tubester.Abstractions.ApplicationConfiguration;
using Tubester.IntegrationTests.TestHost;
using Xunit;

namespace Tubester.IntegrationTests;

/// <summary>
/// Tests for feature flag service.
/// </summary>
[Collection(nameof(TestCollection))]
public class FeatureFlagServiceTests(TestFixture fixture)
{
    [Fact(Skip = "No features yet")]
    public async Task IsEnabledAsync_WhenFeatureExists_ReturnsTrue()
    {
        // Arrange
        await using var scope = fixture.ApiServices.CreateAsyncScope();
        var featureFlagService = scope.ServiceProvider.GetRequiredService<IFeatureFlagService>();

        // Act
        var isEnabled = await featureFlagService.IsEnabledAsync("featureKey", CancellationToken.None);

        // Assert
        Assert.True(isEnabled);
    }

    [Fact]
    public async Task IsEnabledAsync_WhenFeatureDoesNotExist_ReturnsFalse()
    {
        // Arrange
        await using var scope = fixture.ApiServices.CreateAsyncScope();
        var featureFlagService = scope.ServiceProvider.GetRequiredService<IFeatureFlagService>();

        // Act
        var isEnabled = await featureFlagService.IsEnabledAsync("NonExistent.Feature", CancellationToken.None);

        // Assert
        Assert.False(isEnabled);
    }

    [Fact(Skip = "No features yet")]
    public async Task IsEnabledAsync_WithoutPrefix_AddsPrefix()
    {
        // Arrange
        await using var scope = fixture.ApiServices.CreateAsyncScope();
        var featureFlagService = scope.ServiceProvider.GetRequiredService<IFeatureFlagService>();

        // Act - passing "PlaylistSuggest" should be treated as "Feature.PlaylistSuggest"
        var isEnabled = await featureFlagService.IsEnabledAsync("PlaylistSuggest", CancellationToken.None);

        // Assert
        Assert.True(isEnabled);
    }
}