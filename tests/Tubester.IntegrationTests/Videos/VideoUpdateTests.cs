using System.Net;
using Moq;
using Tubester.Application.Contracts.Videos;
using Tubester.Domain;
using Tubester.IntegrationTests.TestHost;
using Xunit;

namespace Tubester.IntegrationTests.Videos;

[Collection(nameof(TestCollection))]
public class VideoUpdateTests(TestFixture fixture)
{
    private readonly TestHelpers _helpers = new(fixture.ApiServices);

    [Fact]
    public async Task UpdateVideo_ValidRequest_CallsYouTubeWithCorrectProperties()
    {
        // Arrange
        await fixture.CleanStateAsync();

        var targetVideo = TestHelpers.GetTargetVideo();
        var playlist1 = TestHelpers.GetPlaylist("PL1");
        var playlist2 = TestHelpers.GetPlaylist("PL2");
        await _helpers.SeedTestDataAsync(new TestDataOptions
        {
            Videos = [targetVideo],
            Playlists = [playlist1, playlist2],
            VideoPlaylists = [VideoPlaylist.Create(targetVideo.VideoId, playlist1.PlaylistId), VideoPlaylist.Create(targetVideo.VideoId, playlist2.PlaylistId)]
        });

        var request = new UpdateVideoMetadataRequest(
            targetVideo.VideoId
        );

        fixture.ApiFactory.MockYouTubeIntegration
            .Setup(youTubeIntegration => youTubeIntegration.UpdateVideoAsync(
                targetVideo.VideoId,
                targetVideo.Title!,
                targetVideo.Description!,
                It.Is<IReadOnlyList<string>>(tags => tags.SequenceEqual(targetVideo.Tags)),
                targetVideo.CategoryId,
                targetVideo.DefaultLanguage,
                targetVideo.DefaultAudioLanguage,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        fixture.ApiFactory.MockYouTubeIntegration
            .Setup(youTubeIntegration => youTubeIntegration.AddVideoToPlaylistAsync(
                playlist1.PlaylistId,
                targetVideo.VideoId,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        fixture.ApiFactory.MockYouTubeIntegration
            .Setup(youTubeIntegration => youTubeIntegration.AddVideoToPlaylistAsync(
                playlist2.PlaylistId,
                targetVideo.VideoId,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var requestMessage = new HttpRequestMessage(HttpMethod.Post, "/api/videos/update")
        {
            Content = TestHelpers.CreateJsonContent(request)
        };

        requestMessage.Headers.Add("OperationId", TestHelpers.NewOperationId());

        var response = await fixture.HttpClient.SendAsync(requestMessage);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var videoDetails = await TestHelpers.DeserializeAsync<VideoDetailsDto>(response);

        var expectedPlaylistDtos = new PlaylistDto[] {
            new() { Id = playlist1.PlaylistId, Name = playlist1.Title },
            new() { Id = playlist2.PlaylistId, Name = playlist2.Title }

        };
        TestHelpers.AssertVideoDetails(videoDetails, targetVideo, expectedPlaylistDtos);
        await _helpers.AssertVideoAsync(targetVideo);
        TestHelpers.SetProperty(targetVideo, nameof(targetVideo.IsDirty), false);
        await _helpers.AssertVideoPlaylistsAsync(targetVideo.VideoId, playlist1.PlaylistId, playlist2.PlaylistId);

        fixture.ApiFactory.MockYouTubeIntegration.Verify(youTubeIntegration =>
                youTubeIntegration.UpdateVideoAsync(
                    targetVideo.VideoId,
                    targetVideo.Title!,
                    targetVideo.Description!,
                    It.Is<IReadOnlyList<string>>(tags => tags.SequenceEqual(targetVideo.Tags)),
                    targetVideo.CategoryId,
                    targetVideo.DefaultLanguage,
                    targetVideo.DefaultAudioLanguage,
                    It.IsAny<CancellationToken>()),
            Times.Once);

        fixture.ApiFactory.MockYouTubeIntegration.Verify(youTubeIntegration =>
                youTubeIntegration.AddVideoToPlaylistAsync(playlist1.PlaylistId, targetVideo.VideoId, It.IsAny<CancellationToken>()),
            Times.Once);

        fixture.ApiFactory.MockYouTubeIntegration.Verify(youTubeIntegration =>
                youTubeIntegration.AddVideoToPlaylistAsync(playlist2.PlaylistId, targetVideo.VideoId, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}