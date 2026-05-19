using System.Net;
using Tubester.Application.Contracts.Videos;
using Tubester.Domain;
using Tubester.IntegrationTests.TestHost;
using Xunit;

namespace Tubester.IntegrationTests.Videos;

[Collection(nameof(TestCollection))]
public class VideoGetTests(TestFixture fixture)
{
    private readonly TestHelpers _helpers = new(fixture.ApiServices);

    [Fact]
    public async Task GetVideo_ExistingVideo_ReturnsOkWithDetails()
    {
        // Arrange
        await fixture.CleanStateAsync();

        var video = TestHelpers.GetTargetVideo();

        await _helpers.SeedTestDataAsync(new TestDataOptions
        {
            Videos = [video]
        });

        // Act
        var response = await fixture.HttpClient.GetAsync($"/api/videos/{TestConstants.TargetVideoId}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var details = await TestHelpers.DeserializeAsync<VideoDetailsDto>(response);

        TestHelpers.AssertVideoDetails(details, video);
    }

    [Fact]
    public async Task GetVideo_WithPlaylist_ReturnsCorrectDetails()
    {
        // Arrange
        await fixture.CleanStateAsync();
        var video = TestHelpers.GetTargetVideo();
        var playlist = TestHelpers.GetPlaylist();

        await _helpers.SeedTestDataAsync(new TestDataOptions
        {
            Videos = [video],
            Playlists = [playlist],
            VideoPlaylists = [VideoPlaylist.Create(video.VideoId, playlist.PlaylistId)]
        });
        // Act
        var response = await fixture.HttpClient.GetAsync($"/api/videos/{TestConstants.TargetVideoId}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var details = await TestHelpers.DeserializeAsync<VideoDetailsDto>(response);

        TestHelpers.AssertVideoDetails(
            details,
            video,
            [new PlaylistDto { Id = playlist.PlaylistId, Name = playlist.Title }]);
    }

    [Fact]
    public async Task GetVideo_VideoDoesNotExist_ReturnsNotFound()
    {
        // Arrange
        await fixture.CleanStateAsync();

        // Act
        var response = await fixture.HttpClient.GetAsync("/api/videos/does-not-exist");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}