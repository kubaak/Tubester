using Microsoft.Extensions.DependencyInjection;
using Moq;
using Tubester.Abstractions;
using Tubester.Abstractions.ApplicationConfiguration;
using Tubester.Application.Jobs;
using Tubester.Domain;
using Tubester.Integration;
using Tubester.Integration.Dtos;
using Tubester.IntegrationTests.TestHost;
using Xunit;

namespace Tubester.IntegrationTests;

/// <summary>
/// Unit tests for GeminiAiClient that use a fake HttpMessageHandler.
/// </summary>
[Collection(nameof(TestCollection))]
public sealed class GeminiAiClientTests : IAsyncLifetime, IDisposable
{
    private readonly CapturingBackgroundJobClient _capturingJobClient = new();
    private readonly WorkerTestHostFactory _factory;
    private readonly TestHelpers _helpers;

    public GeminiAiClientTests()
    {
        _factory = new WorkerTestHostFactory(
            _capturingJobClient,
            DateTimeOffset.UtcNow,
            TestAiMode.Real);
        _helpers = new TestHelpers(_factory.TestHost.Services);
    }

    public Task InitializeAsync()
    {
        return _factory.EnsureDatabaseCreatedAsync();
    }

    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _factory.Dispose();
    }
    
    [Fact (Skip = "Dev")]
    public async Task Factory_ReturnsGemini()
    {
        // Arrange
        await _helpers.ResetDbAsync();
        var configuration = ApplicationConfiguration.Create(ApplicationConfigurationKeys.AiProvider, AiProviders.Gemini,
            ConfigurationValueType.String, null,
            true, TestFixture.TestingDateTimeOffset);
        
        await using var scope = _factory.TestHost.Services.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<GeminiTextGenerationClient>();

        var targetVideo = TestHelpers.GetTargetVideo();
        await _helpers.SeedTestDataAsync(new TestDataOptions
        {
            Videos = [targetVideo],
            ApplicationConfigurations = [configuration]
        });

        const string oldCommentText = "Where was this filmed?";
        const string oldReplyText = "This was filmed in Palermo, Buenos Aires 😊";
        const string newCommentText = "What city are you in?";
        
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
        db.Replies.Add(oldReply);
        await db.SaveChangesAsync();
        oldReply.SetCommentEmbedding(
            embedding.Vector,
            embedding.Model,
            DateTimeOffset.UtcNow.AddDays(-20));
        await db.SaveChangesAsync();
        
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

        // Act & Assert
        Assert.Equal(AiProviders.Gemini, client.Provider);
        Assert.IsType<GeminiTextGenerationClient>(client);
    }
}