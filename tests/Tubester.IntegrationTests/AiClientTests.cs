using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tubester.Abstractions.Playlists;
using Tubester.Integration;
using Xunit;

namespace Tubester.IntegrationTests;

/// <summary>
/// Integration tests for AiClient that call the real AI endpoint via AiClient.SuggestMetadataAsync().
/// These tests require an AI service (Ollama) to be running.
/// </summary>
[Collection(nameof(TestHost.TestCollection))]
public class AiClientTests : IAsyncLifetime
{
    private readonly IServiceProvider _serviceProvider;
    private readonly HttpClient _healthCheckClient;

    public AiClientTests()
    {
        var aiOptions = new AiOptions { Endpoint = "http://localhost:11434" };


        // Create health check client for IAsyncLifetime
        _healthCheckClient = new HttpClient
        {
            BaseAddress = new Uri(aiOptions.Endpoint),
            Timeout = TimeSpan.FromSeconds(10)
        };

        // Set up DI container with the real AiClient
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.Configure<AiOptions>(opts =>
        {
            opts.Endpoint = aiOptions.Endpoint;
            opts.Model = aiOptions.Model;
            opts.Provider = aiOptions.Provider;
        });
        services.AddHttpClient<IAiClient, AiClient>((sp, http) =>
        {
            var options = sp.GetRequiredService<IOptions<AiOptions>>().Value;
            http.BaseAddress = new Uri(options.Endpoint);
            http.Timeout = TimeSpan.FromMinutes(2);
        });
        _serviceProvider = services.BuildServiceProvider();
    }

    public async Task InitializeAsync()
    {
        // Check if AI service is available before running tests
        try
        {
            var healthCheck = await _healthCheckClient.GetAsync("/api/tags");
            if (!healthCheck.IsSuccessStatusCode)
            {
                throw new AiServiceUnavailableException("AI service health check failed");
            }
        }
        catch (HttpRequestException)
        {
            throw new AiServiceUnavailableException("AI service is not available. Ensure Ollama is running at http://localhost:11434");
        }
        catch (TaskCanceledException)
        {
            throw new AiServiceUnavailableException("AI service is not available. Connection timed out.");
        }
        finally
        {
            _healthCheckClient.Dispose();
        }
    }

    public Task DisposeAsync()
    {
        if (_serviceProvider is IDisposable disposable)
        {
            disposable.Dispose();
        }
        return Task.CompletedTask;
    }

    [Fact(Skip = "Dev test")]
    public async Task SuggestMetadata_WithContext_ReturnsTitleDescriptionAndTags()
    {
        // Arrange
        var context = "A tutorial on how to cook the perfect pasta carbonara with authentic Italian ingredients";
        var aiClient = _serviceProvider.GetRequiredService<IAiClient>();

        // Act
        var suggestedMetadata = await aiClient.SuggestMetadataAsync(context, true,
            true, true, CancellationToken.None);
        var title = suggestedMetadata.Title;
        var description = suggestedMetadata.Description;
        var tags = suggestedMetadata.Tags;

        // Assert - Validate title
        Assert.NotNull(title);
        Assert.NotEmpty(title);
        Assert.True(title.Length <= 100, $"Title should be ≤100 characters, but was {title.Length}: {title}");

        // Assert - Validate description
        Assert.NotNull(description);
        Assert.NotEmpty(description);
        Assert.True(description.Length <= 5000, $"Description should be ≤5000 characters, but was {description.Length}");

        // Assert - Validate tags
        Assert.NotNull(tags);
        var tagsList = tags.ToList();
        Assert.NotEmpty(tagsList);
        Assert.All(tagsList, tag => Assert.NotNull(tag));
    }

    [Fact(Skip = "Dev test")]
    public async Task SuggestMetadata_WithTechContext_GeneratesRelevantMetadata()
    {
        // Arrange
        var context = "How to build a REST API with C# and .NET 8 from scratch for beginners";
        var aiClient = _serviceProvider.GetRequiredService<IAiClient>();

        // Act
        var suggestedMetadata = await aiClient.SuggestMetadataAsync(context, true,
            true, true, CancellationToken.None);
        var title = suggestedMetadata.Title;
        var description = suggestedMetadata.Description;
        var tags = suggestedMetadata.Tags;

        // Assert
        Assert.NotNull(title);
        Assert.NotNull(description);
        Assert.NotNull(tags);

        // Title should be reasonably short
        Assert.True(title.Length <= 100, $"Title should be ≤100 characters, but was {title.Length}: {title}");

        // Description should contain some content
        Assert.True(description.Length >= 10, $"Description should be at least 10 chars, but was {description.Length}");

        // Tags should contain relevant programming-related tags
        var tagsLower = tags.Select(t => t?.ToLowerInvariant() ?? "").ToList();
        Assert.Contains(tagsLower, t => t.Contains("api") || t.Contains("rest") || t.Contains("csharp") || t.Contains(".net"));
    }

    [Fact(Skip = "Dev test")]
    public async Task SuggestMetadata_MinimalContext_ReturnsValidMetadata()
    {
        // Arrange - minimal context
        var context = "video about cats";
        var aiClient = _serviceProvider.GetRequiredService<IAiClient>();

        // Act
        var suggestedMetadata = await aiClient.SuggestMetadataAsync(context, true,
            true, true, CancellationToken.None);
        var title = suggestedMetadata.Title;
        var description = suggestedMetadata.Description;
        var tags = suggestedMetadata.Tags;

        // Assert - all fields should still be present even with minimal context
        Assert.NotNull(title);
        Assert.NotNull(description);
        Assert.NotNull(tags);

        // Should have at least one tag
        var tagsList = tags.ToList();
        Assert.NotEmpty(tagsList);
    }

    [Fact(Skip = "Dev test")]
    public async Task SuggestMetadataAsync_WhenOnlyTitleRequested_ReturnsOnlyTitle()
    {
        var aiClient = _serviceProvider.GetRequiredService<IAiClient>();

        var result = await aiClient.SuggestMetadataAsync(
            """
            A travel vlog walking through Cartagena old town, with colorful colonial streets,
            historic buildings, local atmosphere, and sightseeing tips for Colombia.
            """,
            generateTitle: true,
            generateDescription: false,
            generateTags: false,
            CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(result.Title));
        Assert.Null(result.Description);
        Assert.Empty(result.Tags);
    }

    [Fact(Skip = "Dev test")]
    public async Task SuggestMetadataAsync_WhenOnlyDescriptionRequested_ReturnsOnlyDescription()
    {
        var aiClient = _serviceProvider.GetRequiredService<IAiClient>();

        var result = await aiClient.SuggestMetadataAsync(
            """
            A video about the best things to see in Cartagena at night,
            including plazas, city walls, street life, and nightlife atmosphere.
            """,
            generateTitle: false,
            generateDescription: true,
            generateTags: false,
            CancellationToken.None);

        Assert.Null(result.Title);
        Assert.False(string.IsNullOrWhiteSpace(result.Description));
        Assert.Empty(result.Tags);
    }

    [Fact(Skip = "Dev test")]
    public async Task SuggestMetadataAsync_WhenOnlyTagsRequested_ReturnsOnlyTags()
    {
        var aiClient = _serviceProvider.GetRequiredService<IAiClient>();

        var result = await aiClient.SuggestMetadataAsync(
            """
            A video about Castillo de San Felipe de Barajas in Cartagena, Colombia,
            covering the fortress, history, views, and travel tips.
            """,
            generateTitle: false,
            generateDescription: false,
            generateTags: true,
            CancellationToken.None);

        Assert.Null(result.Title);
        Assert.Null(result.Description);
        Assert.NotEmpty(result.Tags);
    }

    [Fact(Skip = "Dev test")]
    public async Task SuggestMetadataAsync_WhenAllRequested_ReturnsAllFields()
    {
        var aiClient = _serviceProvider.GetRequiredService<IAiClient>();

        var result = await aiClient.SuggestMetadataAsync(
            """
            A Cartagena travel video featuring old town streets, food, history,
            local culture, and sightseeing recommendations.
            """,
            generateTitle: true,
            generateDescription: true,
            generateTags: true,
            CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(result.Title));
        Assert.False(string.IsNullOrWhiteSpace(result.Description));
        Assert.NotEmpty(result.Tags);
    }

    [Fact(Skip = "Dev test")]
    public async Task SuggestPlaylistIdsAsync_WithPublicPlaylists_ReturnsSuggestedPlaylistIds()
    {
        // Arrange
        var aiClient = _serviceProvider.GetRequiredService<IAiClient>();

        var playlists = new List<PlaylistCandidateDto>
        {
            new() { PlaylistId = "PLItalianCooking", Name = "Italian Cooking" },
            new() { PlaylistId = "PLPastaRecipes", Name = "Pasta Recipes" },
            new() { PlaylistId = "PLTechReviews", Name = "Tech Reviews" },
            new() { PlaylistId = "PLTravelVlogs", Name = "Travel Vlogs" }
        };
        
        var context = new PlaylistSuggestionContext
        {
            PromptEnrichment = "Italian Cooking",
            LatestPlaylistTitlesUsed = []
        };

        // Act
        var suggestedIds = await aiClient.SuggestPlaylistIdsAsync(context, playlists, CancellationToken.None);

        // Assert
        Assert.NotNull(suggestedIds);

        // Should suggest cooking-related playlists, not tech or travel
        var suggestedList = suggestedIds.ToList();
        Assert.Contains(suggestedList, id => id == "PLItalianCooking" || id == "PLPastaRecipes");
        Assert.DoesNotContain(suggestedList, id => id == "PLTechReviews" || id == "PLTravelVlogs");
    }

    [Fact(Skip = "Dev test")]
    public async Task SuggestPlaylistIdsAsync_WithNoMatchingPlaylists_ReturnsEmptyList()
    {
        // Arrange
        var aiClient = _serviceProvider.GetRequiredService<IAiClient>();


        var playlists = new List<PlaylistCandidateDto>
        {
            new() { PlaylistId = "PLCoffeeRecipes", Name = "Coffee Recipes" },
            new() { PlaylistId = "PLGardeningTips", Name = "Gardening Tips" }
        };
        
        var context = new PlaylistSuggestionContext
        {
            PromptEnrichment = "Gaming",
            LatestPlaylistTitlesUsed = []
        };

        // Act
        var suggestedIds = await aiClient.SuggestPlaylistIdsAsync(context, playlists, CancellationToken.None);

        // Assert
        Assert.NotNull(suggestedIds);
        Assert.Empty(suggestedIds);
    }

    [Fact(Skip = "Dev test")]
    public async Task SuggestPlaylistIdsAsync_WithEmptyPlaylists_ReturnsEmptyList()
    {
        // Arrange
        var aiClient = _serviceProvider.GetRequiredService<IAiClient>();

        var playlists = new List<PlaylistCandidateDto>();

        // Act
        var suggestedIds = await aiClient.SuggestPlaylistIdsAsync(new PlaylistSuggestionContext
        {
            LatestPlaylistTitlesUsed = [],
            PromptEnrichment = "Cooking"
        }, playlists, CancellationToken.None);

        // Assert
        Assert.NotNull(suggestedIds);
        Assert.Empty(suggestedIds);
    }

    [Fact(Skip = "Dev test")]
    public async Task SuggestPlaylistIdsAsync_WithLastUsedPlaylists_InfluencesSuggestions()
    {
        // Arrange
        var aiClient = _serviceProvider.GetRequiredService<IAiClient>();

        var playlists = new List<PlaylistCandidateDto>
        {
            new() { PlaylistId = "PLTechReviews", Name = "Tech Reviews" },
            new() { PlaylistId = "PLTravelVlogs", Name = "Travel Vlogs" },
            new() { PlaylistId = "PLColombia", Name = "Exploring Colombia" },
            new() { PlaylistId = "PLItalianCooking", Name = "Italian Cooking" },
            new() { PlaylistId = "PLPastaRecipes", Name = "Pasta Recipes" },
            new() { PlaylistId = "PLGaming", Name = "Gaming" },
        };
        
        var context = new PlaylistSuggestionContext
        {
            PromptEnrichment = "Cartagena",
            LatestPlaylistTitlesUsed = new List<string> { "Travel Vlogs", "Exploring Colombia" }
        };

        // Act
        var suggestedIds = await aiClient.SuggestPlaylistIdsAsync(context, playlists, CancellationToken.None);

        // Assert
        Assert.NotNull(suggestedIds);
        var suggestedList = suggestedIds.ToList();
        
        Assert.NotEmpty(suggestedList);
        Assert.Contains(suggestedList, id => id == "PLTravelVlogs" || id == "PLColombia");
    }

    /// <summary>
    /// Exception thrown when AI service is unavailable for testing.
    /// </summary>
    private class AiServiceUnavailableException(string message) : Exception(message);
}
