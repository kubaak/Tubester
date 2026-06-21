using System.Net;
using Tubester.Abstractions.Videos;
using Tubester.IntegrationTests.TestHost;
using Xunit;

namespace Tubester.IntegrationTests.Videos;

[Collection(nameof(TestCollection))]
public class DirtyTests(TestFixture fixture)
{
    private readonly TestHelpers _helpers = new(fixture.ApiServices);

    [Fact]
    public async Task Dirty_EmptyDb_ReturnsEmptyList()
    {
        // Arrange
        await fixture.CleanStateAsync();
        await _helpers.SeedTestDataAsync();

        // Act
        var response = await fixture.HttpClient.GetAsync("/api/videos/dirty");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await TestHelpers.DeserializeAsync<IReadOnlyList<VideoListItemDto>>(response);

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task Dirty_WithDirtyVideos_ReturnsOnlyDirtyVideos()
    {
        // Arrange
        await fixture.CleanStateAsync();

        var dirtyVideo1 = TestHelpers.GetTargetVideo("dirty-video-1");
        dirtyVideo1.MarkAsDirty(TestFixture.TestingDateTimeOffset);
        var dirtyVideo2 = TestHelpers.GetTargetVideo("dirty-video-2");
        dirtyVideo2.MarkAsDirty(TestFixture.TestingDateTimeOffset);
        var cleanVideo = TestHelpers.GetTargetVideo("clean-video");

        await _helpers.SeedTestDataAsync(new TestDataOptions
        {
            Videos = [dirtyVideo1, dirtyVideo2, cleanVideo]
        });

        // Act
        var response = await fixture.HttpClient.GetAsync("/api/videos/dirty");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await TestHelpers.DeserializeAsync<IReadOnlyList<VideoListItemDto>>(response);

        Assert.NotNull(result);
        Assert.Equal(2, result.Count);

        Assert.Contains(result, item => item.VideoId == dirtyVideo1.VideoId);
        Assert.Contains(result, item => item.VideoId == dirtyVideo2.VideoId);
        Assert.DoesNotContain(result, item => item.VideoId == cleanVideo.VideoId);

        TestHelpers.AssertVideoListItemDto(
            Assert.Single(result, item => item.VideoId == dirtyVideo1.VideoId),
            dirtyVideo1);

        TestHelpers.AssertVideoListItemDto(
            Assert.Single(result, item => item.VideoId == dirtyVideo2.VideoId),
            dirtyVideo2);
    }

    [Fact]
    public async Task Dirty_ReturnsVideosOrderedByPublishedAtDescendingThenVideoIdDescending()
    {
        // Arrange
        await fixture.CleanStateAsync();

        var oldestVideo = TestHelpers.GetTargetVideo("dirty-video-a");
        var newestVideo = TestHelpers.GetTargetVideo("dirty-video-b");
        var sameDateHigherVideoId = TestHelpers.GetTargetVideo("dirty-video-d");
        var sameDateLowerVideoId = TestHelpers.GetTargetVideo("dirty-video-c");

        TestHelpers.SetProperty(
            oldestVideo,
            nameof(oldestVideo.PublishedAt),
            TestFixture.TestingDateTimeOffset.AddDays(-10));

        TestHelpers.SetProperty(
            newestVideo,
            nameof(newestVideo.PublishedAt),
            TestFixture.TestingDateTimeOffset.AddDays(-1));

        TestHelpers.SetProperty(
            sameDateHigherVideoId,
            nameof(sameDateHigherVideoId.PublishedAt),
            TestFixture.TestingDateTimeOffset.AddDays(-5));

        TestHelpers.SetProperty(
            sameDateLowerVideoId,
            nameof(sameDateLowerVideoId.PublishedAt),
            TestFixture.TestingDateTimeOffset.AddDays(-5));

        TestHelpers.SetProperty(oldestVideo, nameof(oldestVideo.IsDirty), true);
        TestHelpers.SetProperty(newestVideo, nameof(newestVideo.IsDirty), true);
        TestHelpers.SetProperty(sameDateHigherVideoId, nameof(sameDateHigherVideoId.IsDirty), true);
        TestHelpers.SetProperty(sameDateLowerVideoId, nameof(sameDateLowerVideoId.IsDirty), true);

        await _helpers.SeedTestDataAsync(new TestDataOptions
        {
            Videos = [oldestVideo, newestVideo, sameDateLowerVideoId, sameDateHigherVideoId]
        });

        // Act
        var response = await fixture.HttpClient.GetAsync("/api/videos/dirty");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await TestHelpers.DeserializeAsync<IReadOnlyList<VideoListItemDto>>(response);

        Assert.NotNull(result);
        Assert.Equal(4, result.Count);

        Assert.Equal(newestVideo.VideoId, result[0].VideoId);
        Assert.Equal(sameDateHigherVideoId.VideoId, result[1].VideoId);
        Assert.Equal(sameDateLowerVideoId.VideoId, result[2].VideoId);
        Assert.Equal(oldestVideo.VideoId, result[3].VideoId);
    }
}
