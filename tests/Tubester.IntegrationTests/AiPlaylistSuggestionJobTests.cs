using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Tubester.Abstractions.ApplicationConfiguration;
using Tubester.Abstractions.Videos;
using Tubester.Application.Jobs;
using Tubester.Domain;
using Tubester.Integration;
using Tubester.IntegrationTests.TestHost;
using Xunit;

namespace Tubester.IntegrationTests;

[Collection(nameof(TestCollection))]
public class AiPlaylistSuggestionJobTests(TestFixture fixture)
{
    private readonly TestHelpers _helpers = new(fixture.WorkerServices);

    private const string PromptEnrichment = "Suggest relevant playlists for this video";

    private const string SuggestedPlaylistId1 = "playlist-suggested-1";
    private const string SuggestedPlaylistId2 = "playlist-suggested-2";

    private const string PlaylistId1 = "playlist-id-1";
    private const string PlaylistId2 = "playlist-id-2";
    private const string PlaylistId3 = "playlist-id-3";

    private const string SimulatedAiTextGenerationClientFailureMessage = "Simulated AI client failure";

    [Fact]
    public async Task Run_WithValidRequest_AssignsVideoToSuggestedPlaylists()
    {
        // Arrange
        await fixture.CleanStateAsync();

        var targetVideo = TestHelpers.GetTargetVideo();
        var playlist1 = TestHelpers.GetPlaylist(PlaylistId1);
        var playlist2 = TestHelpers.GetPlaylist(PlaylistId2);
        var playlist3 = TestHelpers.GetPlaylist(PlaylistId3);

        var testData = await _helpers.SeedTestDataAsync(new TestDataOptions
        {
            Videos = [targetVideo],
            Playlists = [playlist1, playlist2, playlist3]
        });

        var jsonResponse = JsonSerializer.Serialize(new { i = new List<int> { 0, 2 } });

        fixture.WorkerFactory.MockAiTextGenerationClient
            .Setup(client => client.GenerateTextAsync(
                AiOperation.PlaylistSuggestion,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateMockResult(jsonResponse));

        var request = new PlaylistSuggestionRequest(
            testData.Channel.ChannelId,
            testData.Channel.UploadsPlaylistId,
            targetVideo.VideoId,
            PromptEnrichment);

        using var scope = fixture.WorkerServices.CreateScope();
        var videoRepository = scope.ServiceProvider.GetRequiredService<IVideoRepository>();
        await videoRepository.TryAddAiOperationsInProgressAsync(testData.Channel.UploadsPlaylistId, targetVideo.VideoId,
            AiVideoOperationFlags.PlaylistSuggestion, CancellationToken.None);
        // Act
        var aiPlaylistSuggestionJob = scope.ServiceProvider.GetRequiredService<AiPlaylistSuggestionJob>();
        await aiPlaylistSuggestionJob.Run(request, new Hangfire.JobCancellationToken(false));

        // Assert
        await _helpers.AssertVideoAsync(targetVideo);
        await _helpers.AssertVideoPlaylistsAsync(targetVideo.VideoId, PlaylistId1, PlaylistId3);

        fixture.WorkerFactory.MockAiTextGenerationClient.Verify(
            client => client.GenerateTextAsync(
                AiOperation.PlaylistSuggestion,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Run_WhenNoPlaylistsExist_ExitsEarly()
    {
        // Arrange
        await fixture.CleanStateAsync();

        var targetVideo = TestHelpers.GetTargetVideo();

        await _helpers.SeedTestDataAsync(new TestDataOptions
        {
            Videos = [targetVideo],
            Playlists = [] // No playlists
        });

        var request = new PlaylistSuggestionRequest(
            TestConstants.ChannelId,
            TestConstants.UploadsPlaylistId,
            targetVideo.VideoId,
            PromptEnrichment);

        // Act
        using var jobScope = fixture.WorkerServices.CreateScope();
        var aiPlaylistSuggestionJob = jobScope.ServiceProvider.GetRequiredService<AiPlaylistSuggestionJob>();
        await aiPlaylistSuggestionJob.Run(request, new Hangfire.JobCancellationToken(false));

        // Assert - No playlists should be assigned
        await _helpers.AssertVideoPlaylistsAsync(targetVideo.VideoId);

        // AI client should NOT be called when there are no playlists
        fixture.WorkerFactory.MockAiTextGenerationClient.Verify(
            client => client.GenerateTextAsync(
                AiOperation.PlaylistSuggestion,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Run_WhenAiTextGenerationClientReturnsNoSuggestions_DoesNotAssignPlaylists()
    {
        // Arrange
        await fixture.CleanStateAsync();
        

        var targetVideo = TestHelpers.GetTargetVideo();
        var playlist1 = TestHelpers.GetPlaylist(PlaylistId1);
        var playlist2 = TestHelpers.GetPlaylist(PlaylistId2);

        await _helpers.SeedTestDataAsync(new TestDataOptions
        {
            Videos = [targetVideo],
            Playlists = [playlist1, playlist2]
        });

        var jsonResponse = JsonSerializer.Serialize(new { i = new List<int>() });

        fixture.WorkerFactory.MockAiTextGenerationClient
            .Setup(client => client.GenerateTextAsync(
                AiOperation.PlaylistSuggestion,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateMockResult(jsonResponse));

        var request = new PlaylistSuggestionRequest(
            TestConstants.ChannelId,
            TestConstants.UploadsPlaylistId,
            targetVideo.VideoId,
            PromptEnrichment);

        // Act
        using var jobScope = fixture.WorkerServices.CreateScope();
        var aiPlaylistSuggestionJob = jobScope.ServiceProvider.GetRequiredService<AiPlaylistSuggestionJob>();
        await aiPlaylistSuggestionJob.Run(request, new Hangfire.JobCancellationToken(false));

        // Assert - No playlists should be assigned
        await _helpers.AssertVideoPlaylistsAsync(targetVideo.VideoId);

        fixture.WorkerFactory.MockAiTextGenerationClient.Verify(
            client => client.GenerateTextAsync(
                AiOperation.PlaylistSuggestion,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Run_WhenAiTextGenerationClientThrows_PropagatesException()
    {
        // Arrange
        await fixture.CleanStateAsync();
        

        var targetVideo = TestHelpers.GetTargetVideo();
        var playlist1 = TestHelpers.GetPlaylist(PlaylistId1);

        await _helpers.SeedTestDataAsync(new TestDataOptions
        {
            Videos = [targetVideo],
            Playlists = [playlist1]
        });

        fixture.WorkerFactory.MockAiTextGenerationClient
            .Setup(client => client.GenerateTextAsync(
                It.IsAny<AiOperation>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception(SimulatedAiTextGenerationClientFailureMessage));

        var request = new PlaylistSuggestionRequest(
            TestConstants.ChannelId,
            TestConstants.UploadsPlaylistId,
            targetVideo.VideoId,
            PromptEnrichment);

        // Act & Assert
        using var jobScope = fixture.WorkerServices.CreateScope();
        var aiPlaylistSuggestionJob = jobScope.ServiceProvider.GetRequiredService<AiPlaylistSuggestionJob>();

        var exception = await Assert.ThrowsAsync<Exception>(() =>
            aiPlaylistSuggestionJob.Run(request, new Hangfire.JobCancellationToken(false)));

        Assert.Equal(SimulatedAiTextGenerationClientFailureMessage, exception.Message);

        // Verify no playlists were assigned after the exception
        await _helpers.AssertVideoPlaylistsAsync(targetVideo.VideoId);
    }

    [Fact]
    public async Task Run_WithMultiplePlaylists_ProcessesInBatches()
    {
        // Arrange
        await fixture.CleanStateAsync();
        

        var targetVideo = TestHelpers.GetTargetVideo();
        var playlists = new List<Playlist>
        {
            TestHelpers.GetPlaylist("PL-BATCH-1"),
            TestHelpers.GetPlaylist("PL-BATCH-2"),
            TestHelpers.GetPlaylist("PL-BATCH-3")
        };

        await _helpers.SeedTestDataAsync(new TestDataOptions
        {
            Videos = [targetVideo],
            Playlists = playlists
        });

        var jsonResponse = JsonSerializer.Serialize(new { i = new List<int> { 0 } });

        fixture.WorkerFactory.MockAiTextGenerationClient
            .Setup(client => client.GenerateTextAsync(
                AiOperation.PlaylistSuggestion,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateMockResult(jsonResponse));

        var request = new PlaylistSuggestionRequest(
            TestConstants.ChannelId,
            TestConstants.UploadsPlaylistId,
            targetVideo.VideoId,
            PromptEnrichment);

        // Act
        using var jobScope = fixture.WorkerServices.CreateScope();
        var aiPlaylistSuggestionJob = jobScope.ServiceProvider.GetRequiredService<AiPlaylistSuggestionJob>();
        await aiPlaylistSuggestionJob.Run(request, new Hangfire.JobCancellationToken(false));

        // Assert - AI client should be called once per batch
        fixture.WorkerFactory.MockAiTextGenerationClient.Verify(
            client => client.GenerateTextAsync(
                AiOperation.PlaylistSuggestion,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Run_WithInvalidIndexes_IgnoresThemAndAssignsValidOnes()
    {
        // Arrange
        await fixture.CleanStateAsync();
        

        var targetVideo = TestHelpers.GetTargetVideo();
        var playlist1 = TestHelpers.GetPlaylist(PlaylistId1);
        var playlist2 = TestHelpers.GetPlaylist(PlaylistId2);
        var playlist3 = TestHelpers.GetPlaylist(PlaylistId3);

        await _helpers.SeedTestDataAsync(new TestDataOptions
        {
            Videos = [targetVideo],
            Playlists = [playlist1, playlist2, playlist3]
        });

        // Indexes: 0 (valid), 5 (out of bounds), -1 (negative)
        var jsonResponse = JsonSerializer.Serialize(new { i = new List<int> { 0, 5, -1, 2 } });

        fixture.WorkerFactory.MockAiTextGenerationClient
            .Setup(client => client.GenerateTextAsync(
                AiOperation.PlaylistSuggestion,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateMockResult(jsonResponse));

        var request = new PlaylistSuggestionRequest(
            TestConstants.ChannelId,
            TestConstants.UploadsPlaylistId,
            targetVideo.VideoId,
            PromptEnrichment);

        // Act
        using var jobScope = fixture.WorkerServices.CreateScope();
        var aiPlaylistSuggestionJob = jobScope.ServiceProvider.GetRequiredService<AiPlaylistSuggestionJob>();
        await aiPlaylistSuggestionJob.Run(request, new Hangfire.JobCancellationToken(false));

        // Assert - Only valid indexes 0 and 2 should be assigned
        await _helpers.AssertVideoPlaylistsAsync(targetVideo.VideoId, PlaylistId1, PlaylistId3);
    }

    [Fact]
    public async Task Run_WithDuplicateIndexes_DeduplicatesAndAssignsOnce()
    {
        // Arrange
        await fixture.CleanStateAsync();
        

        var targetVideo = TestHelpers.GetTargetVideo();
        var playlist1 = TestHelpers.GetPlaylist(PlaylistId1);
        var playlist2 = TestHelpers.GetPlaylist(PlaylistId2);

        await _helpers.SeedTestDataAsync(new TestDataOptions
        {
            Videos = [targetVideo],
            Playlists = [playlist1, playlist2]
        });

        // Indexes: 0, 0, 1, 1 (duplicates)
        var jsonResponse = JsonSerializer.Serialize(new { i = new List<int> { 0, 0, 1, 1 } });

        fixture.WorkerFactory.MockAiTextGenerationClient
            .Setup(client => client.GenerateTextAsync(
                AiOperation.PlaylistSuggestion,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateMockResult(jsonResponse));

        var request = new PlaylistSuggestionRequest(
            TestConstants.ChannelId,
            TestConstants.UploadsPlaylistId,
            targetVideo.VideoId,
            PromptEnrichment);

        // Act
        using var jobScope = fixture.WorkerServices.CreateScope();
        var aiPlaylistSuggestionJob = jobScope.ServiceProvider.GetRequiredService<AiPlaylistSuggestionJob>();
        await aiPlaylistSuggestionJob.Run(request, new Hangfire.JobCancellationToken(false));

        // Assert - Each playlist should appear only once
        await _helpers.AssertVideoPlaylistsAsync(targetVideo.VideoId, PlaylistId1, PlaylistId2);
    }
}
