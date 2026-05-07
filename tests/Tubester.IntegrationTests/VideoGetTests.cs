using System.Net;
using System.Text.Json;
using Tubester.Application.Contracts.Videos;
using Tubester.Domain;
using Tubester.IntegrationTests.TestHost;
using Xunit;

namespace Tubester.IntegrationTests;

[Collection(nameof(TestCollection))]
public class VideoGetTests(TestFixture fixture)
{
    private readonly TestHelpers _helpers = new(fixture);

    [Fact]
    public async Task GetVideo_ExistingVideo_ReturnsOkWithDetails()
    {
        // Arrange
        await fixture.ResetDbAsync();

        var video = TestHelpers.GetTargetVideo();

        await _helpers.SeedVideoTestDataAsync(new TestDataOptions
        {
            Videos = [video]
        });

        // Act
        var response = await fixture.HttpClient.GetAsync($"/api/videos/{TestConstants.TargetVideoId}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();

        var videoDetails = JsonSerializer.Deserialize<VideoDetailsDto>(
            content,
            TestHelpers.SerializerOptions);

        TestHelpers.AssertVideoDetails(videoDetails, video);
    }

    [Fact]
    public async Task GetVideo_WithPlaylist_ReturnsCorrectDetails()
    {
        // Arrange
        await fixture.ResetDbAsync();
        var video = TestHelpers.GetTargetVideo();
        var playlist = TestHelpers.GetPlaylist();

        await _helpers.SeedVideoTestDataAsync(new TestDataOptions
        {
            Videos = [video],
            Playlists = [playlist],
            VideoPlaylists = [VideoPlaylist.Create(video.VideoId, playlist.PlaylistId)]
        });
        // Act
        var response = await fixture.HttpClient.GetAsync($"/api/videos/{TestConstants.TargetVideoId}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();

        var videoDetails = JsonSerializer.Deserialize<VideoDetailsDto>(
            content,
            TestHelpers.SerializerOptions);

        TestHelpers.AssertVideoDetails(
            videoDetails,
            video,
            [new PlaylistDto { Id = playlist.PlaylistId, Name = playlist.Title }]);
    }

    [Fact]
    public async Task GetVideo_VideoDoesNotExist_ReturnsNotFound()
    {
        // Arrange
        await fixture.ResetDbAsync();

        // Act
        var response = await fixture.HttpClient.GetAsync("/api/videos/does-not-exist");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}