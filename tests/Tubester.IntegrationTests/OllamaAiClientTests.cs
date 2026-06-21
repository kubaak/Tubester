using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Tubester.Abstractions;
using Tubester.Abstractions.ApplicationConfiguration;
using Tubester.Abstractions.Playlists;
using Tubester.Application.Jobs;
using Tubester.Domain;
using Tubester.Integration;
using Tubester.Integration.Dtos;
using Tubester.IntegrationTests.TestHost;
using Xunit;

namespace Tubester.IntegrationTests;

/// <summary>
/// Integration tests for AiClient that call the real Ollama endpoint.
/// These tests require Ollama to be running.
/// </summary>
public sealed class OllamaAiClientTests : IAsyncLifetime, IDisposable
{
    private const string OllamaEndpoint = "http://localhost:11434";

    private readonly CapturingBackgroundJobClient _capturingJobClient = new();
    private readonly WorkerTestHostFactory _factory;
    private readonly HttpClient _healthCheckClient;
    private readonly TestHelpers _helpers;

    public OllamaAiClientTests()
    {
        _factory = new WorkerTestHostFactory(
            _capturingJobClient,
            DateTimeOffset.UtcNow,
            TestAiMode.Real);
        _helpers = new TestHelpers(_factory.TestHost.Services);

        _healthCheckClient = new HttpClient
        {
            BaseAddress = new Uri(OllamaEndpoint),
            Timeout = TimeSpan.FromSeconds(10)
        };
    }

    public async Task InitializeAsync()
    {
        try
        {
            var healthCheck = await _healthCheckClient.GetAsync("/api/tags");

            if (!healthCheck.IsSuccessStatusCode)
            {
                throw new AiServiceUnavailableException("AI service health check failed.");
            }
        }
        catch (HttpRequestException)
        {
            throw new AiServiceUnavailableException(
                $"AI service is not available. Ensure Ollama is running at {OllamaEndpoint}.");
        }
        catch (TaskCanceledException)
        {
            throw new AiServiceUnavailableException("AI service is not available. Connection timed out.");
        }

        await _factory.EnsureDatabaseCreatedAsync();
    }

    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _healthCheckClient.Dispose();
        _factory.Dispose();
    }

    [Fact(Skip = "Dev")]
    public async Task SuggestMetadata_WithContext_ReturnsTitleDescriptionAndTags()
    {
        await _helpers.ResetDbAsync();
        await _helpers.SeedTestDataAsync();
        const string context = "A tutorial on how to cook the perfect pasta carbonara with authentic Italian ingredients";
        using var scope = _factory.TestHost.Services.CreateScope();
        var aiClient = scope.ServiceProvider.GetRequiredService<IAiClient>();
        var suggestedMetadata = await aiClient.SuggestMetadataAsync(
            context,
            generateTitle: true,
            generateDescription: true,
            generateTags: true,
            CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(suggestedMetadata.Title));
        Assert.True(suggestedMetadata.Title.Length <= 100);

        Assert.False(string.IsNullOrWhiteSpace(suggestedMetadata.Description));
        Assert.True(suggestedMetadata.Description.Length <= 5000);

        Assert.NotEmpty(suggestedMetadata.Tags);
        Assert.All(suggestedMetadata.Tags, tag => Assert.False(string.IsNullOrWhiteSpace(tag)));
    }

    [Fact(Skip = "Dev")]
    public async Task SuggestMetadata_WithTechContext_GeneratesRelevantMetadata()
    {
        await _helpers.ResetDbAsync();
        await _helpers.SeedTestDataAsync();
        const string context = "How to build a REST API with C# and .NET 8 from scratch for beginners";
        using var scope = _factory.TestHost.Services.CreateScope();
        var aiClient = scope.ServiceProvider.GetRequiredService<IAiClient>();

        var result = await aiClient.SuggestMetadataAsync(
            context,
            generateTitle: true,
            generateDescription: true,
            generateTags: true,
            CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(result.Title));
        Assert.True(result.Title.Length <= 100);

        Assert.False(string.IsNullOrWhiteSpace(result.Description));
        Assert.True(result.Description.Length >= 10);

        Assert.NotEmpty(result.Tags);

        var tagsLower = result.Tags
            .Select(t => t.ToLowerInvariant())
            .ToList();

        Assert.Contains(tagsLower, t =>
            t.Contains("api") ||
            t.Contains("rest") ||
            t.Contains("csharp") ||
            t.Contains(".net"));
    }

    [Fact(Skip = "Dev")]
    public async Task SuggestMetadata_MinimalContext_ReturnsValidMetadata()
    {
        await _helpers.ResetDbAsync();
        await _helpers.SeedTestDataAsync();
        using var scope = _factory.TestHost.Services.CreateScope();
        var aiClient = scope.ServiceProvider.GetRequiredService<IAiClient>();

        var result = await aiClient.SuggestMetadataAsync(
            "video about cats",
            generateTitle: true,
            generateDescription: true,
            generateTags: true,
            CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(result.Title));
        Assert.False(string.IsNullOrWhiteSpace(result.Description));
        Assert.NotEmpty(result.Tags);
    }

    [Fact(Skip = "Dev")]
    public async Task SuggestMetadataAsync_WhenOnlyTitleRequested_ReturnsOnlyTitle()
    {
        await _helpers.ResetDbAsync();
        await _helpers.SeedTestDataAsync();
        using var scope = _factory.TestHost.Services.CreateScope();
        var aiClient = scope.ServiceProvider.GetRequiredService<IAiClient>();

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

    [Fact(Skip = "Dev")]
    public async Task SuggestMetadataAsync_WhenOnlyDescriptionRequested_ReturnsOnlyDescription()
    {
        await _helpers.ResetDbAsync();
        await _helpers.SeedTestDataAsync();
        using var scope = _factory.TestHost.Services.CreateScope();
        var aiClient = scope.ServiceProvider.GetRequiredService<IAiClient>();

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

    [Fact(Skip = "Dev")]
    public async Task SuggestMetadataAsync_WhenOnlyTagsRequested_ReturnsOnlyTags()
    {
        await _helpers.ResetDbAsync();
        await _helpers.SeedTestDataAsync();
        using var scope = _factory.TestHost.Services.CreateScope();
        var aiClient = scope.ServiceProvider.GetRequiredService<IAiClient>();

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

    [Fact(Skip = "Dev")]
    public async Task SuggestMetadataAsync_WhenAllRequested_ReturnsAllFields()
    {
        await _helpers.ResetDbAsync();
        await _helpers.SeedTestDataAsync();
        using var scope = _factory.TestHost.Services.CreateScope();
        var aiClient = scope.ServiceProvider.GetRequiredService<IAiClient>();

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

    [Fact(Skip = "Dev")]
    public async Task SuggestPlaylistIdsAsync_WithPublicPlaylists_ReturnsSuggestedPlaylistIds()
    {
        await _helpers.ResetDbAsync();
        await _helpers.SeedTestDataAsync();
        using var scope = _factory.TestHost.Services.CreateScope();
        var aiClient = scope.ServiceProvider.GetRequiredService<IAiClient>();

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

        var suggestedIds = await aiClient.SuggestPlaylistIdsAsync(
            context,
            playlists,
            CancellationToken.None);

        var suggestedList = suggestedIds.ToList();

        Assert.Contains(suggestedList, id => id == "PLItalianCooking" || id == "PLPastaRecipes");
        Assert.DoesNotContain(suggestedList, id => id == "PLTechReviews" || id == "PLTravelVlogs");
    }

    [Fact(Skip = "Dev")]
    public async Task SuggestPlaylistIdsAsync_WithNoMatchingPlaylists_ReturnsEmptyList()
    {
        await _helpers.ResetDbAsync();
        await _helpers.SeedTestDataAsync();
        using var scope = _factory.TestHost.Services.CreateScope();
        var aiClient = scope.ServiceProvider.GetRequiredService<IAiClient>();

        var playlists = new List<PlaylistCandidateDto>
        {
            new() { PlaylistId = "PLCoffeeRecipes", Name = "Coffee Recipes" },
            new() { PlaylistId = "PLGardeningTips", Name = "Gardening Tips" },
            new() { PlaylistId = "PLItalianCooking", Name = "Italian Cooking" },
            new() { PlaylistId = "PLPastaRecipes", Name = "Pasta Recipes" },
            new() { PlaylistId = "PLTechReviews", Name = "Dance Shows" },
            new() { PlaylistId = "PLTravelVlogs", Name = "Travel Vlogs" }
        };

        var context = new PlaylistSuggestionContext
        {
            PromptEnrichment = "Gaming",
            LatestPlaylistTitlesUsed = []
        };

        var suggestedIds = await aiClient.SuggestPlaylistIdsAsync(
            context,
            playlists,
            CancellationToken.None);

        Assert.Empty(suggestedIds);
    }

    [Fact(Skip = "Dev")]
    public async Task SuggestPlaylistIdsAsync_WithEmptyPlaylists_ReturnsEmptyList()
    {
        await _helpers.ResetDbAsync();
        await _helpers.SeedTestDataAsync();
        using var scope = _factory.TestHost.Services.CreateScope();
        var aiClient = scope.ServiceProvider.GetRequiredService<IAiClient>();

        var suggestedIds = await aiClient.SuggestPlaylistIdsAsync(
            new PlaylistSuggestionContext
            {
                LatestPlaylistTitlesUsed = [],
                PromptEnrichment = "Cooking"
            },
            [],
            CancellationToken.None);

        Assert.Empty(suggestedIds);
    }

    [Fact(Skip = "Dev")]
    public async Task SuggestPlaylistIdsAsync_WithLastUsedPlaylists_InfluencesSuggestions()
    {
        await _helpers.ResetDbAsync();
        await _helpers.SeedTestDataAsync();
        using var scope = _factory.TestHost.Services.CreateScope();
        var aiClient = scope.ServiceProvider.GetRequiredService<IAiClient>();

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
            PromptEnrichment = "Cartagena Vlog",
            LatestPlaylistTitlesUsed = ["Travel Vlogs", "Exploring Colombia"]
        };

        var suggestedIds = await aiClient.SuggestPlaylistIdsAsync(
            context,
            playlists,
            CancellationToken.None);

        var suggestedList = suggestedIds.ToList();

        Assert.NotEmpty(suggestedList);
        Assert.Contains(suggestedList, id => id == "PLTravelVlogs");
        Assert.Contains(suggestedList, id => id == "PLColombia");
    }

    [Fact(Skip = "Dev")]
    public async Task SuggestPlaylistIdsAsync_WithRealData()
    {
        await _helpers.ResetDbAsync();
        var testOptions = new TestDataOptions();
        testOptions.ApplicationConfigurations.Add(
            ApplicationConfiguration.Create(ApplicationConfigurationKeys.AiPlaylistTemperature, "0.1", ConfigurationValueType.Decimal, "", false, TestFixture.TestingDateTimeOffset)
        );
        await _helpers.SeedTestDataAsync();
        using var scope = _factory.TestHost.Services.CreateScope();
        var aiClient = scope.ServiceProvider.GetRequiredService<IAiClient>();

        var playlists = new List<PlaylistCandidateDto>
        {
            new() { PlaylistId = "PLkF34pCo5F9txUy-Bd7h3hEXUlzaMLn-O", Name = "Jízda" },
            new() { PlaylistId = "PLkF34pCo5F9tYKDRv9QGyPy75VoZTJQMX", Name = "Video Tutorial" },
            new() { PlaylistId = "PLkF34pCo5F9t1AwsgQOb0rPn9JDOcH2bF", Name = "Workshops" },
            new() { PlaylistId = "PLkF34pCo5F9tEI0lQRYzWsAxbqgLy1Ae2", Name = "Showcase" },
            new() { PlaylistId = "PLkF34pCo5F9tyFlfSdyfEE4YblR7GVPUR", Name = "Ostatní" },
            new() { PlaylistId = "PLkF34pCo5F9tHh-7N-DtePaOfb2RmYj5O", Name = "dotNet" },
            new() { PlaylistId = "PLkF34pCo5F9vgCePou8kA-31n8ZZRaQEP", Name = "Fitness" },
            new() { PlaylistId = "PLkF34pCo5F9sjP23tptC2G1YlFrm9gXBD", Name = "Bboy Tutorials" },
            new() { PlaylistId = "PLkF34pCo5F9tEsUfOPN6unH5JROn08VbK", Name = "Caliesthenics" },
            new() { PlaylistId = "PLkF34pCo5F9sDJ0-uX51cyEJ6tDQ3FEVA", Name = "Trénink Plzeň" },
            new() { PlaylistId = "PLkF34pCo5F9soI1oy5Srdsribkl6WTYXg", Name = "Trénink Klatovy" },
            new() { PlaylistId = "PLkF34pCo5F9uAgmv14TfcCGAdeb1R7yca", Name = "Morroco 🇲🇦" },
            new() { PlaylistId = "PLkF34pCo5F9ts93Y6-1XHABSv1rDGA6Y9", Name = "Sessions" },
            new() { PlaylistId = "PLkF34pCo5F9tfFpw-SkTXAHI8wscrz-CM", Name = "Portugal 🇵🇹" },
            new() { PlaylistId = "PLkF34pCo5F9thKstxuIQS2uTKHCCpkQen", Name = "Brazil 🇧🇷" },
            new() { PlaylistId = "PLkF34pCo5F9toYudZ_c26Yz-D1_nLhucR", Name = "Inspirace" },
            new() { PlaylistId = "PLkF34pCo5F9vUcU37Dg0F5tzRTWqBiEOz", Name = "Parties" },
            new() { PlaylistId = "PLkF34pCo5F9vFi_3U76UAFW52wG6L7EKC", Name = "Jízda" },
            new() { PlaylistId = "PLkF34pCo5F9v0LIej_46adVe4IsZpYdmb", Name = "Nemovitosti" },
            new() { PlaylistId = "PLkF34pCo5F9uvM4_Aw-GLaQC4YszcOa-p", Name = "Furious Day" },
            new() { PlaylistId = "PLkF34pCo5F9s3vGrir7mUNImtUDXk6jD2", Name = "Breaking Shorts 🤸" },
            new() { PlaylistId = "PLkF34pCo5F9vxk_5csJR-6_QfZAjFOdlS", Name = "The Legits Blast" },
            new() { PlaylistId = "PLkF34pCo5F9vXOuyp8hEtpkc7O1QaHpii", Name = "Bachata" },
            new() { PlaylistId = "PLkF34pCo5F9u9U3yXjw_MOZbpWqUTNVGU", Name = "Czech Breaking Camp 2021" },
            new() { PlaylistId = "PLkF34pCo5F9vCjAs8C_AJ0YlQXuF2UpPL", Name = "Air Flare" },
            new() { PlaylistId = "PLkF34pCo5F9vLNV1aKw6lE65d16QR9CcC", Name = "Old Soul" },
            new() { PlaylistId = "PLkF34pCo5F9vchBduN8eMUpDUMx_4Z7_M", Name = "IBE 2018" },
            new() { PlaylistId = "PLkF34pCo5F9vfJ5ZEUBjllKtIgrG60ds5", Name = "Travel 🌎" },
            new() { PlaylistId = "PLkF34pCo5F9v7Dfkoce4jcZakSlBJ0CQh", Name = "Shit Happens Crew" },
            new() { PlaylistId = "PLkF34pCo5F9uhtDrS8c8CPHzYV7tuduMH", Name = "Soustředění Shit Happens 2012" },
            new() { PlaylistId = "PLkF34pCo5F9uZSSYoBoMFoTCM6fvycDvR", Name = "Soustředění Shit Happens 2013" },
            new() { PlaylistId = "PLkF34pCo5F9vCedJzfSwby1TDyabuz-0S", Name = "Colombia 🇨🇴" },
            new() { PlaylistId = "PLkF34pCo5F9ujQZoateuh7ZX3PeKUo9D4", Name = "Spain 🇪🇸" },
            new() { PlaylistId = "PLkF34pCo5F9tuOE7dziPBBVkvFTPlS5Uz", Name = "Argentina 🇦🇷" },
            new() { PlaylistId = "PLkF34pCo5F9uyNtzsZFoyUk6ZAg8SU5Fe", Name = "Soutěže" },
            new() { PlaylistId = "PLkF34pCo5F9udxzYIZXqo7-Yffwx8956E", Name = "Battles 🤸" },
            new() { PlaylistId = "PLkF34pCo5F9uSDmI-Scgg9EgWwwwG2ffy", Name = "Showcase SH" },
            new() { PlaylistId = "PLkF34pCo5F9ucphK82Bd7URcdAWYGbfJA", Name = "Promo" },
            new() { PlaylistId = "PLkF34pCo5F9u1Bk2yAUkGUtocn9hZcbE4", Name = "Soustředění Shit Happens 2015" },
            new() { PlaylistId = "PLkF34pCo5F9uuVDeK8-mG4efdafsPFfN1", Name = "PL/SQL" },
            new() { PlaylistId = "PLkF34pCo5F9uHefHNipV7olKfA3V-BnAO", Name = "Music" },
            new() { PlaylistId = "PLkF34pCo5F9v0ucAtuLbq_b3U-R_IGKom", Name = "six pack" },
            new() { PlaylistId = "PLkF34pCo5F9uWJUGrfG5Tw3qVFudIwsch", Name = "belly" },
            new() { PlaylistId = "PLkF34pCo5F9vpvu7KOcMQnt-q4YgjpHi1", Name = "Crazy Juices" },
            new() { PlaylistId = "PL2F39F6F96DE4D5C0", Name = "stará" },
            new() { PlaylistId = "FLYJ7iCgKapwGYc2r2LKlVoQ", Name = "Favorites" }
        };

        var context = new PlaylistSuggestionContext
        {
            PromptEnrichment = "Discovering Getsemaní Cartagena",
            LatestPlaylistTitlesUsed = ["Colombia 🇨🇴", "Travel 🌎"]
        };

        var suggestedIds = await aiClient.SuggestPlaylistIdsAsync(
            context,
            playlists,
            CancellationToken.None);

        var suggestedList = suggestedIds.ToList();

        Assert.Contains(suggestedList, id =>
            id is "PLkF34pCo5F9vfJ5ZEUBjllKtIgrG60ds5"); // Travel 🌎
        Assert.Contains(suggestedList, id =>
            id is "PLkF34pCo5F9vCedJzfSwby1TDyabuz-0S"); //  Colombia 🇨🇴

        // Assert.True(suggestedList.Count <= 2);
    }

    [Fact(Skip = "Dev")]
    public async Task SuggestReplyAsync_WithValidComment_ReturnsReply()
    {
        await _helpers.ResetDbAsync();
        await _helpers.SeedTestDataAsync();
        using var scope = _factory.TestHost.Services.CreateScope();
        var aiClient = scope.ServiceProvider.GetRequiredService<IAiClient>();

        var reply = await aiClient.SuggestReplyAsync(
            "How to Cook Perfect Pasta",
            "This is an amazing recipe! I tried it and it turned out great.",
            "en",
            null,
            CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(reply));
    }

    [Fact(Skip = "Dev")]
    public async Task SuggestReplyAsync_WithEmptyTags_ReturnsReply()
    {
        await _helpers.ResetDbAsync();
        await _helpers.SeedTestDataAsync();
        using var scope = _factory.TestHost.Services.CreateScope();
        var aiClient = scope.ServiceProvider.GetRequiredService<IAiClient>();

        var reply = await aiClient.SuggestReplyAsync(
            "Dance Tutorial",
            "Love the choreography! When will you post the next part?",
            "en",
            null,
            CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(reply));
    }

    [Fact(Skip = "Dev")]
    public async Task SuggestReplyAsync_WithEmptyVideoTitle_ReturnsReply()
    {
        await _helpers.ResetDbAsync();
        await _helpers.SeedTestDataAsync();
        using var scope = _factory.TestHost.Services.CreateScope();
        var aiClient = scope.ServiceProvider.GetRequiredService<IAiClient>();

        var reply = await aiClient.SuggestReplyAsync(
            string.Empty,
            "This move is amazing! Can you slow it down for practice?",
            "en",
            null,
            CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(reply));
    }

    [Fact(Skip = "Dev")]
    public async Task CommentScanJob_WithSimilarPreviousReply_PassesRelevantExampleToLlm()
    {
        // Arrange
        await _helpers.ResetDbAsync();

        var targetVideo = TestHelpers.GetTargetVideo();
        await _helpers.SeedTestDataAsync(new TestDataOptions
        {
            Videos = [targetVideo]
        });

        const string oldCommentText = "Where was this filmed?";
        const string oldReplyText = "This was filmed in Palermo, Buenos Aires 😊";
        const string newCommentText = "What city are you in?";
        
        using (var scope = _factory.TestHost.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Persistence.TubesterDb>();
            var embeddingServiceFactory = scope.ServiceProvider.GetRequiredService<IEmbeddingServiceFactory>();
            var embeddingService = await embeddingServiceFactory.GetServiceAsync(CancellationToken.None);
            
            var embedding = await embeddingService.EmbedAsync(oldCommentText, CancellationToken.None);

            var oldReply = Reply.Create(
                "old-comment-id",
                targetVideo.VideoId,
                targetVideo.Title ?? string.Empty,
                oldCommentText,
                DateTimeOffset.UtcNow.AddDays(-20),
                DateTimeOffset.UtcNow.AddDays(-20));

            oldReply.SuggestText(oldReplyText, DateTimeOffset.UtcNow.AddDays(-20));
            oldReply.ApproveText(TestConstants.UserId, oldReplyText, DateTimeOffset.UtcNow.AddDays(-20));
            oldReply.Post(TestConstants.UserId, DateTimeOffset.UtcNow.AddDays(-20));
            oldReply.SetCommentEmbedding(
                embedding.Vector,
                embedding.Model,
                DateTimeOffset.UtcNow.AddDays(-20));

            db.Replies.Add(oldReply);
            await db.SaveChangesAsync();
        }

        var newComment = new CommentThreadDto(
            "new-comment-id",
            targetVideo.VideoId,
            "viewer-1",
            newCommentText,
            DateTimeOffset.UtcNow.AddDays(-1));

        _factory.MockBackgroundYoutubeIntegration
            .Setup(x => x.GetUnansweredTopLevelCommentsAsync(
                TestConstants.ChannelId,
                targetVideo.VideoId,
                It.IsAny<CancellationToken>()))
            .Returns(new[] { newComment }.ToAsyncEnumerable());

        // Act
        using var jobScope = _factory.TestHost.Services.CreateScope();
        var commentScanJob = jobScope.ServiceProvider.GetRequiredService<CommentScanJob>();

        await commentScanJob.Run(
            TestConstants.ChannelId,
            null,
            new Hangfire.JobCancellationToken(false));

        // Assert
        using var verifyScope = _factory.TestHost.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<Persistence.TubesterDb>();

        var createdReply = await verifyDb.Replies
            .AsNoTracking()
            .SingleAsync(r => r.CommentId == "new-comment-id");

        Assert.NotNull(createdReply.SuggestedText);
        Assert.NotNull(createdReply.CommentEmbedding);
    }

    private sealed class AiServiceUnavailableException(string message) : Exception(message);
}