using Microsoft.Extensions.DependencyInjection;
using Moq;
using Tubester.Abstractions.Playlists;
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
    private readonly TestHelpers _helpers = new(fixture);

    private const string PromptEnrichment = "Suggest relevant playlists for this video";

    private const string SuggestedPlaylistId1 = "playlist-suggested-1";
    private const string SuggestedPlaylistId2 = "playlist-suggested-2";

    private const string PlaylistId1 = "playlist-id-1";
    private const string PlaylistId2 = "playlist-id-2";
    private const string PlaylistId3 = "playlist-id-3";

    private const string SimulatedAiClientFailureMessage = "Simulated AI client failure";

    [Fact]
    public async Task Run_WithValidRequest_AssignsVideoToSuggestedPlaylists()
    {
        // Arrange
        await fixture.ResetDbAsync();
        fixture.WorkerFactory.MockAiClient.Invocations.Clear();

        var targetVideo = TestHelpers.GetTargetVideo();
        var playlist1 = TestHelpers.GetPlaylist(PlaylistId1);
        var playlist2 = TestHelpers.GetPlaylist(PlaylistId2);
        var playlist3 = TestHelpers.GetPlaylist(PlaylistId3);

        var testData = await _helpers.SeedVideoTestDataAsync(new TestDataOptions
        {
            Videos = [targetVideo],
            Playlists = [playlist1, playlist2, playlist3]
        });

        fixture.WorkerFactory.MockAiClient
            .Setup(client => client.SuggestPlaylistIdsAsync(
                It.IsAny<PlaylistSuggestionContext>(),
                It.IsAny<IReadOnlyList<PlaylistCandidateDto>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { PlaylistId1, PlaylistId3 });

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

        fixture.WorkerFactory.MockAiClient.Verify(
            client => client.SuggestPlaylistIdsAsync(
                It.IsAny<PlaylistSuggestionContext>(),
                It.IsAny<IReadOnlyList<PlaylistCandidateDto>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Run_WhenNoPlaylistsExist_ExitsEarly()
    {
        // Arrange
        await fixture.ResetDbAsync();
        fixture.WorkerFactory.MockAiClient.Invocations.Clear();

        var targetVideo = TestHelpers.GetTargetVideo();

        await _helpers.SeedVideoTestDataAsync(new TestDataOptions
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
        fixture.WorkerFactory.MockAiClient.Verify(
            client => client.SuggestPlaylistIdsAsync(
                It.IsAny<PlaylistSuggestionContext>(),
                It.IsAny<IReadOnlyList<PlaylistCandidateDto>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Run_WhenAiClientReturnsNoSuggestions_DoesNotAssignPlaylists()
    {
        // Arrange
        await fixture.ResetDbAsync();
        fixture.WorkerFactory.MockAiClient.Invocations.Clear();

        var targetVideo = TestHelpers.GetTargetVideo();
        var playlist1 = TestHelpers.GetPlaylist(PlaylistId1);
        var playlist2 = TestHelpers.GetPlaylist(PlaylistId2);

        await _helpers.SeedVideoTestDataAsync(new TestDataOptions
        {
            Videos = [targetVideo],
            Playlists = [playlist1, playlist2]
        });

        fixture.WorkerFactory.MockAiClient
            .Setup(client => client.SuggestPlaylistIdsAsync(
                It.IsAny<PlaylistSuggestionContext>(),
                It.IsAny<IReadOnlyList<PlaylistCandidateDto>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>()); // Return empty suggestions

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

        fixture.WorkerFactory.MockAiClient.Verify(
            client => client.SuggestPlaylistIdsAsync(
                It.IsAny<PlaylistSuggestionContext>(),
                It.IsAny<IReadOnlyList<PlaylistCandidateDto>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Run_WhenAiClientThrows_PropagatesException()
    {
        // Arrange
        await fixture.ResetDbAsync();
        fixture.WorkerFactory.MockAiClient.Invocations.Clear();

        var targetVideo = TestHelpers.GetTargetVideo();
        var playlist1 = TestHelpers.GetPlaylist(PlaylistId1);

        await _helpers.SeedVideoTestDataAsync(new TestDataOptions
        {
            Videos = [targetVideo],
            Playlists = [playlist1]
        });

        fixture.WorkerFactory.MockAiClient
            .Setup(client => client.SuggestPlaylistIdsAsync(
                It.IsAny<PlaylistSuggestionContext>(),
                It.IsAny<IReadOnlyList<PlaylistCandidateDto>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception(SimulatedAiClientFailureMessage));

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

        Assert.Equal(SimulatedAiClientFailureMessage, exception.Message);

        // Verify no playlists were assigned after the exception
        await _helpers.AssertVideoPlaylistsAsync(targetVideo.VideoId);
    }

    [Fact]
    public async Task Run_WithMultiplePlaylists_ProcessesInBatches()
    {
        // Arrange
        await fixture.ResetDbAsync();
        fixture.WorkerFactory.MockAiClient.Invocations.Clear();

        var targetVideo = TestHelpers.GetTargetVideo();
        var playlists = new List<Playlist>
        {
            TestHelpers.GetPlaylist("PL-BATCH-1"),
            TestHelpers.GetPlaylist("PL-BATCH-2"),
            TestHelpers.GetPlaylist("PL-BATCH-3")
        };

        await _helpers.SeedVideoTestDataAsync(new TestDataOptions
        {
            Videos = [targetVideo],
            Playlists = playlists
        });

        fixture.WorkerFactory.MockAiClient
            .Setup(client => client.SuggestPlaylistIdsAsync(
                It.IsAny<PlaylistSuggestionContext>(),
                It.IsAny<IReadOnlyList<PlaylistCandidateDto>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { SuggestedPlaylistId1 });

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
        fixture.WorkerFactory.MockAiClient.Verify(
            client => client.SuggestPlaylistIdsAsync(
                It.IsAny<PlaylistSuggestionContext>(),
                It.IsAny<IReadOnlyList<PlaylistCandidateDto>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}