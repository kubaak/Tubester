using System.Net;
using Google.Apis.YouTube.v3;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Tubester.Abstractions.Auth;
using Tubester.Integration;
using Tubester.IntegrationTests.TestHost;
using Xunit;

namespace Tubester.IntegrationTests;

public class YouTubeIntegrationTests
{
    private const string TestAccessToken = "test-access-token";

    [Fact]
    public async Task GetPlaylistVideoIdsAsync_WhenPlaylistExists_ReturnsVideoIds()
    {
        // Arrange
        var testContext = CreateTestContext();

        testContext.YouTubeHttpHandler.EnqueueJson(
            HttpStatusCode.OK,
            YouTubeJson.PlaylistItems("video-1", "video-2"));

        // Act
        var result = await testContext.Integration
            .GetPlaylistVideoIdsAsync("playlist-1", CancellationToken.None)
            .ToListAsync();

        // Assert
        Assert.Equal(["video-1", "video-2"], result);

        Assert.Contains(
            testContext.YouTubeServiceFactory.Calls,
            call => call.AccessToken == TestAccessToken &&
                    call.Scope == YouTubeService.Scope.YoutubeReadonly);

        Assert.Contains(
            testContext.YouTubeHttpHandler.Requests,
            request => request.RequestUri?.AbsoluteUri.Contains("playlistItems") == true);
    }

    [Fact]
    public async Task GetVideosAsync_MapsVideoPropertiesCorrectly()
    {
        // Arrange
        var testContext = CreateTestContext();

        testContext.YouTubeHttpHandler.EnqueueJson(
            HttpStatusCode.OK,
            YouTubeJson.VideosList("test-video-1"));

        // Act
        var result = await testContext.Integration.GetVideosAsync(["test-video-1"], CancellationToken.None);

        // Assert
        Assert.Single(result);

        var video = result[0];

        Assert.Equal("test-video-1", video.VideoId);
        Assert.Equal("Title test-video-1", video.Title);
        Assert.Equal("Description test-video-1", video.Description);
        Assert.Contains("tag1", video.Tags!);
        Assert.Contains("tag2", video.Tags!);
        Assert.Equal(TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(10), video.Duration);
        Assert.Equal("public", video.PrivacyStatus);
        Assert.Equal("22", video.CategoryId);
    }

    private static YouTubeIntegrationTestContext CreateTestContext(string? accessToken = TestAccessToken)
    {
        var youTubeHttpHandler = new StubHttpMessageHandler();
        var youTubeServiceFactory = new TestYouTubeServiceFactory(youTubeHttpHandler);

        var currentUserTokenAccessor = new Mock<ICurrentUserTokenAccessor>(MockBehavior.Strict);
        currentUserTokenAccessor
            .Setup(accessor => accessor.GetAccessTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(accessToken);

        var integration = new YouTubeIntegration(
            currentUserTokenAccessor.Object,
            youTubeServiceFactory,
            NullLogger<YouTubeIntegration>.Instance);

        return new YouTubeIntegrationTestContext(
            integration,
            youTubeHttpHandler,
            youTubeServiceFactory,
            currentUserTokenAccessor);
    }

    private sealed record YouTubeIntegrationTestContext(
        YouTubeIntegration Integration,
        StubHttpMessageHandler YouTubeHttpHandler,
        TestYouTubeServiceFactory YouTubeServiceFactory,
        Mock<ICurrentUserTokenAccessor> CurrentUserTokenAccessor);
}