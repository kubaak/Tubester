using System.Net;
using Moq;
using Tubester.Application.Contracts.Videos;
using Tubester.Domain;
using Tubester.Integration.Dtos;
using Tubester.IntegrationTests.TestHost;
using Xunit;

namespace Tubester.IntegrationTests.Videos;

[Collection(nameof(TestCollection))]
public class VideoResyncTests(TestFixture fixture)
{
    private readonly TestHelpers _helpers = new(fixture.ApiServices);

    [Fact]
    public async Task ResyncVideo_ExistingVideo_UpdatesDetailsAndPlaylistsFromYoutube()
    {
        // Arrange
        await fixture.CleanStateAsync();

        var video = TestHelpers.GetTargetVideo();
        var oldPlaylist = TestHelpers.GetPlaylist("old-playlist");
        var currentPlaylist = TestHelpers.GetPlaylist("current-playlist");

        await _helpers.SeedTestDataAsync(new TestDataOptions
        {
            Videos = [video],
            Playlists = [oldPlaylist, currentPlaylist],
            VideoPlaylists =
            [
                VideoPlaylist.Create(video.VideoId, oldPlaylist.PlaylistId)
            ]
        });

        const VideoVisibility remoteVideoVisibility = VideoVisibility.Unlisted;
        var remoteVideo = new VideoDto(
            video.VideoId,
            "Remote Title",
            "Remote Description",
            ["remote-tag-1", "remote-tag-2"],
            video.Duration,
            remoteVideoVisibility.ToString().ToLower(),
            false,
            TestFixture.TestingDateTimeOffset.AddDays(-3),
            "24",
            "fr",
            "fr",
            "remote-etag",
            true
        );

        fixture.ApiFactory.MockYouTubeIntegration
            .Setup(youTubeIntegration => youTubeIntegration.GetVideosAsync(
                It.Is<IEnumerable<string>>(videoIds => videoIds.SequenceEqual(new[] { video.VideoId })),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([remoteVideo]);

        fixture.ApiFactory.MockYouTubeIntegration
            .Setup(youTubeIntegration => youTubeIntegration.PlaylistContainsVideoAsync(
                oldPlaylist.PlaylistId,
                video.VideoId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        fixture.ApiFactory.MockYouTubeIntegration
            .Setup(youTubeIntegration => youTubeIntegration.PlaylistContainsVideoAsync(
                currentPlaylist.PlaylistId,
                video.VideoId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var response = await fixture.HttpClient.PostAsync($"/api/videos/resync?videoId={video.VideoId}", null);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var videoDetails = await TestHelpers.DeserializeAsync<VideoDetailsDto>(response);
        TestHelpers.SetVideoProperties(video, remoteVideo.Title, remoteVideo.Description, remoteVideo.Tags!.ToArray());
        TestHelpers.SetProperty(video, nameof(video.Visibility), remoteVideoVisibility);
        TestHelpers.SetProperty(video, nameof(video.PublishedAt), remoteVideo.PublishedAt);
        TestHelpers.SetProperty(video, nameof(video.CategoryId), remoteVideo.CategoryId);
        TestHelpers.SetProperty(video, nameof(video.DefaultAudioLanguage), remoteVideo.DefaultAudioLanguage);
        TestHelpers.SetProperty(video, nameof(video.DefaultLanguage), remoteVideo.DefaultLanguage);
        TestHelpers.SetProperty(video, nameof(video.ETag), remoteVideo.ETag);
        TestHelpers.SetProperty(video, nameof(video.IsDirty), false);
        TestHelpers.SetProperty(video, nameof(video.CachedAt), TestFixture.TestingDateTimeOffset);

        await _helpers.AssertVideoAsync(video);

        var expectedPlaylistDtos = new PlaylistDto[]
        {
            new() { Id = currentPlaylist.PlaylistId, Name = currentPlaylist.Title }
        };

        TestHelpers.AssertVideoDetails(videoDetails, video, expectedPlaylistDtos);
        await _helpers.AssertVideoPlaylistsAsync(video.VideoId, currentPlaylist.PlaylistId);

        fixture.ApiFactory.MockYouTubeIntegration.Verify(youTubeIntegration =>
                youTubeIntegration.GetVideosAsync(
                    It.Is<IEnumerable<string>>(videoIds => videoIds.SequenceEqual(new[] { video.VideoId })),
                    It.IsAny<CancellationToken>()),
            Times.Once);

        fixture.ApiFactory.MockYouTubeIntegration.Verify(youTubeIntegration =>
                youTubeIntegration.PlaylistContainsVideoAsync(
                    oldPlaylist.PlaylistId,
                    video.VideoId,
                    It.IsAny<CancellationToken>()),
            Times.Once);

        fixture.ApiFactory.MockYouTubeIntegration.Verify(youTubeIntegration =>
                youTubeIntegration.PlaylistContainsVideoAsync(
                    currentPlaylist.PlaylistId,
                    video.VideoId,
                    It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ResyncVideo_ExistingDirtyVideo_UpdatesDetailsAndPlaylistsFromYoutube()
    {
        // Arrange
        await fixture.CleanStateAsync();

        var video = TestHelpers.GetTargetVideo();
        video.MarkAsDirty(TestFixture.TestingDateTimeOffset);

        var oldPlaylist = TestHelpers.GetPlaylist("old-playlist");
        var currentPlaylist = TestHelpers.GetPlaylist("current-playlist");

        await _helpers.SeedTestDataAsync(new TestDataOptions
        {
            Videos = [video],
            Playlists = [oldPlaylist, currentPlaylist],
            VideoPlaylists =
            [
                VideoPlaylist.Create(video.VideoId, oldPlaylist.PlaylistId)
            ]
        });

        const VideoVisibility remoteVideoVisibility = VideoVisibility.Unlisted;
        var remoteVideo = new VideoDto(
            video.VideoId,
            "Remote Title",
            "Remote Description",
            ["remote-tag-1", "remote-tag-2"],
            video.Duration,
            remoteVideoVisibility.ToString().ToLower(),
            false,
            TestFixture.TestingDateTimeOffset.AddDays(-3),
            "24",
            "fr",
            "fr",
            "remote-etag",
            true
        );

        fixture.ApiFactory.MockYouTubeIntegration
            .Setup(youTubeIntegration => youTubeIntegration.GetVideosAsync(
                It.Is<IEnumerable<string>>(videoIds => videoIds.SequenceEqual(new[] { video.VideoId })),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([remoteVideo]);

        fixture.ApiFactory.MockYouTubeIntegration
            .Setup(youTubeIntegration => youTubeIntegration.PlaylistContainsVideoAsync(
                oldPlaylist.PlaylistId,
                video.VideoId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        fixture.ApiFactory.MockYouTubeIntegration
            .Setup(youTubeIntegration => youTubeIntegration.PlaylistContainsVideoAsync(
                currentPlaylist.PlaylistId,
                video.VideoId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var response = await fixture.HttpClient.PostAsync($"/api/videos/resync?videoId={video.VideoId}", null);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var videoDetails = await TestHelpers.DeserializeAsync<VideoDetailsDto>(response);
        TestHelpers.SetVideoProperties(video, remoteVideo.Title, remoteVideo.Description, remoteVideo.Tags!.ToArray());
        TestHelpers.SetProperty(video, nameof(video.Visibility), remoteVideoVisibility);
        TestHelpers.SetProperty(video, nameof(video.PublishedAt), remoteVideo.PublishedAt);
        TestHelpers.SetProperty(video, nameof(video.CategoryId), remoteVideo.CategoryId);
        TestHelpers.SetProperty(video, nameof(video.DefaultAudioLanguage), remoteVideo.DefaultAudioLanguage);
        TestHelpers.SetProperty(video, nameof(video.DefaultLanguage), remoteVideo.DefaultLanguage);
        TestHelpers.SetProperty(video, nameof(video.ETag), remoteVideo.ETag);
        TestHelpers.SetProperty(video, nameof(video.IsDirty), false);
        TestHelpers.SetProperty(video, nameof(video.CachedAt), TestFixture.TestingDateTimeOffset);

        await _helpers.AssertVideoAsync(video);

        var expectedPlaylistDtos = new PlaylistDto[]
        {
            new() { Id = currentPlaylist.PlaylistId, Name = currentPlaylist.Title }
        };

        TestHelpers.AssertVideoDetails(videoDetails, video, expectedPlaylistDtos);
        await _helpers.AssertVideoPlaylistsAsync(video.VideoId, currentPlaylist.PlaylistId);

        fixture.ApiFactory.MockYouTubeIntegration.Verify(youTubeIntegration =>
                youTubeIntegration.GetVideosAsync(
                    It.Is<IEnumerable<string>>(videoIds => videoIds.SequenceEqual(new[] { video.VideoId })),
                    It.IsAny<CancellationToken>()),
            Times.Once);

        fixture.ApiFactory.MockYouTubeIntegration.Verify(youTubeIntegration =>
                youTubeIntegration.PlaylistContainsVideoAsync(
                    oldPlaylist.PlaylistId,
                    video.VideoId,
                    It.IsAny<CancellationToken>()),
            Times.Once);

        fixture.ApiFactory.MockYouTubeIntegration.Verify(youTubeIntegration =>
                youTubeIntegration.PlaylistContainsVideoAsync(
                    currentPlaylist.PlaylistId,
                    video.VideoId,
                    It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ResyncVideo_ExistingDirtyVideo_SameEtag_UpdatesDetailsAndPlaylistsFromYoutube()
    {
        // Arrange
        await fixture.CleanStateAsync();

        var video = TestHelpers.GetTargetVideo();
        video.MarkAsDirty(TestFixture.TestingDateTimeOffset);

        var oldPlaylist = TestHelpers.GetPlaylist("old-playlist");
        var currentPlaylist = TestHelpers.GetPlaylist("current-playlist");

        await _helpers.SeedTestDataAsync(new TestDataOptions
        {
            Videos = [video],
            Playlists = [oldPlaylist, currentPlaylist],
            VideoPlaylists =
            [
                VideoPlaylist.Create(video.VideoId, oldPlaylist.PlaylistId)
            ]
        });

        const VideoVisibility remoteVideoVisibility = VideoVisibility.Unlisted;
        var remoteVideo = new VideoDto(
            video.VideoId,
            "Remote Title",
            "Remote Description",
            ["remote-tag-1", "remote-tag-2"],
            video.Duration,
            remoteVideoVisibility.ToString().ToLower(),
            false,
            TestFixture.TestingDateTimeOffset.AddDays(-3),
            "24",
            "fr",
            "fr",
            video.ETag,
            true
        );

        fixture.ApiFactory.MockYouTubeIntegration
            .Setup(youTubeIntegration => youTubeIntegration.GetVideosAsync(
                It.Is<IEnumerable<string>>(videoIds => videoIds.SequenceEqual(new[] { video.VideoId })),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([remoteVideo]);

        fixture.ApiFactory.MockYouTubeIntegration
            .Setup(youTubeIntegration => youTubeIntegration.PlaylistContainsVideoAsync(
                oldPlaylist.PlaylistId,
                video.VideoId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        fixture.ApiFactory.MockYouTubeIntegration
            .Setup(youTubeIntegration => youTubeIntegration.PlaylistContainsVideoAsync(
                currentPlaylist.PlaylistId,
                video.VideoId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var response = await fixture.HttpClient.PostAsync($"/api/videos/resync?videoId={video.VideoId}", null);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var videoDetails = await TestHelpers.DeserializeAsync<VideoDetailsDto>(response);
        TestHelpers.SetVideoProperties(video, remoteVideo.Title, remoteVideo.Description, remoteVideo.Tags!.ToArray());
        TestHelpers.SetProperty(video, nameof(video.Visibility), remoteVideoVisibility);
        TestHelpers.SetProperty(video, nameof(video.PublishedAt), remoteVideo.PublishedAt);
        TestHelpers.SetProperty(video, nameof(video.CategoryId), remoteVideo.CategoryId);
        TestHelpers.SetProperty(video, nameof(video.DefaultAudioLanguage), remoteVideo.DefaultAudioLanguage);
        TestHelpers.SetProperty(video, nameof(video.DefaultLanguage), remoteVideo.DefaultLanguage);
        TestHelpers.SetProperty(video, nameof(video.ETag), remoteVideo.ETag);
        TestHelpers.SetProperty(video, nameof(video.IsDirty), false);
        TestHelpers.SetProperty(video, nameof(video.CachedAt), TestFixture.TestingDateTimeOffset);

        await _helpers.AssertVideoAsync(video);

        var expectedPlaylistDtos = new PlaylistDto[]
        {
            new() { Id = currentPlaylist.PlaylistId, Name = currentPlaylist.Title }
        };

        TestHelpers.AssertVideoDetails(videoDetails, video, expectedPlaylistDtos);
        await _helpers.AssertVideoPlaylistsAsync(video.VideoId, currentPlaylist.PlaylistId);

        fixture.ApiFactory.MockYouTubeIntegration.Verify(youTubeIntegration =>
                youTubeIntegration.GetVideosAsync(
                    It.Is<IEnumerable<string>>(videoIds => videoIds.SequenceEqual(new[] { video.VideoId })),
                    It.IsAny<CancellationToken>()),
            Times.Once);

        fixture.ApiFactory.MockYouTubeIntegration.Verify(youTubeIntegration =>
                youTubeIntegration.PlaylistContainsVideoAsync(
                    oldPlaylist.PlaylistId,
                    video.VideoId,
                    It.IsAny<CancellationToken>()),
            Times.Once);

        fixture.ApiFactory.MockYouTubeIntegration.Verify(youTubeIntegration =>
                youTubeIntegration.PlaylistContainsVideoAsync(
                    currentPlaylist.PlaylistId,
                    video.VideoId,
                    It.IsAny<CancellationToken>()),
            Times.Once);
    }
}