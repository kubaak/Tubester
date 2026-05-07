using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Tubester.Abstractions.ApplicationConfiguration;
using Tubester.Domain;
using Tubester.IntegrationTests.TestHost;
using Xunit;

namespace Tubester.IntegrationTests;

[Collection(nameof(TestCollection))]
public class AiRuntimeOptionsServiceTests(TestFixture fixture)
{
    private readonly TestHelpers _helpers = new(fixture.WorkerServices);

    [Fact]
    public async Task GetAsync_WithStoredMetadataAndPlaylistValues_ReturnsValuesForEachOperation()
    {
        await fixture.CleanStateAsync();
        var testDataOptions = new TestDataOptions
        {
            ApplicationConfigurations =
            [
                ApplicationConfiguration.Create(
                    ApplicationConfigurationKeys.AiProvider,
                    AiProviders.Gemini,
                    ConfigurationValueType.String,
                    "AI provider",
                    true,
                    TestFixture.TestingDateTimeOffset),
                ApplicationConfiguration.Create(
                    ApplicationConfigurationKeys.AiModel,
                    "metadata-model",
                    ConfigurationValueType.String,
                    "AI model for metadata and replies",
                    true,
                    TestFixture.TestingDateTimeOffset),
                ApplicationConfiguration.Create(
                    ApplicationConfigurationKeys.AiTemperature,
                    "0.31",
                    ConfigurationValueType.Decimal,
                    "AI temperature for metadata and replies",
                    true,
                    TestFixture.TestingDateTimeOffset),
                ApplicationConfiguration.Create(
                    ApplicationConfigurationKeys.AiPlaylistModel,
                    "playlist-model",
                    ConfigurationValueType.String,
                    "AI model for playlist suggestion",
                    true,
                    TestFixture.TestingDateTimeOffset),
                ApplicationConfiguration.Create(
                    ApplicationConfigurationKeys.AiPlaylistTemperature,
                    "0.62",
                    ConfigurationValueType.Decimal,
                    "AI temperature for playlist suggestion",
                    true,
                    TestFixture.TestingDateTimeOffset),
                ApplicationConfiguration.Create(
                    ApplicationConfigurationKeys.AiDetailsMaxOutputTokens,
                    "333",
                    ConfigurationValueType.Integer,
                    "Maximum output tokens for AI details generation",
                    true,
                    TestFixture.TestingDateTimeOffset),
                ApplicationConfiguration.Create(
                    ApplicationConfigurationKeys.AiPlaylistMaxOutputTokens,
                    "444",
                    ConfigurationValueType.Integer,
                    "AI temperature for playlist suggestion",
                    true,
                    TestFixture.TestingDateTimeOffset),
            ]
        };

        await _helpers.SeedTestDataAsync(testDataOptions);
        ClearConfigurationCache(fixture.WorkerServices);

        using var serviceScope = fixture.WorkerServices.CreateScope();
        var aiRuntimeOptionsService = serviceScope.ServiceProvider.GetRequiredService<IAiRuntimeOptionsService>();

        var metadataRuntimeOptions = await aiRuntimeOptionsService.GetAsync(AiOperation.Metadata, CancellationToken.None);
        var playlistRuntimeOptions = await aiRuntimeOptionsService.GetAsync(AiOperation.PlaylistSuggestion, CancellationToken.None);

        Assert.Equal(AiProviders.Gemini, metadataRuntimeOptions.Provider);
        Assert.Equal("metadata-model", metadataRuntimeOptions.Model);
        Assert.Equal(0.31, metadataRuntimeOptions.Temperature);
        Assert.Equal(333, metadataRuntimeOptions.MaxOutputTokens);

        Assert.Equal(AiProviders.Gemini, playlistRuntimeOptions.Provider);
        Assert.Equal("playlist-model", playlistRuntimeOptions.Model);
        Assert.Equal(0.62, playlistRuntimeOptions.Temperature);
        Assert.Equal(444, playlistRuntimeOptions.MaxOutputTokens);
    }

    private static void ClearConfigurationCache(IServiceProvider serviceProvider)
    {
        using var serviceScope = serviceProvider.CreateScope();
        var memoryCache = serviceScope.ServiceProvider.GetRequiredService<IMemoryCache>();

        memoryCache.Remove($"config:{ApplicationConfigurationKeys.AiProvider}");
        memoryCache.Remove($"config:{ApplicationConfigurationKeys.AiModel}");
        memoryCache.Remove($"config:{ApplicationConfigurationKeys.AiTemperature}");
        memoryCache.Remove($"config:{ApplicationConfigurationKeys.AiPlaylistModel}");
        memoryCache.Remove($"config:{ApplicationConfigurationKeys.AiPlaylistTemperature}");
        memoryCache.Remove($"config:{ApplicationConfigurationKeys.AiDetailsMaxOutputTokens}");
    }
}
