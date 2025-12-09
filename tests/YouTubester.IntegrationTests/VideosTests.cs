using System.Net;
using System.Text;
using System.Text.Json;
using AutoFixture;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;
using YouTubester.Application;
using YouTubester.Application.Contracts;
using YouTubester.Application.Contracts.Videos;
using YouTubester.Domain;
using YouTubester.IntegrationTests.TestHost;
using YouTubester.Persistence;
using StringContent = System.Net.Http.StringContent;

namespace YouTubester.IntegrationTests;

[Collection(nameof(TestCollection))]
public class VideosTests(TestFixture fixture)
{
    private readonly JsonSerializerOptions _serializerOptions =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    [Fact]
    public async Task GetVideos_EmptyDb_ReturnsEmptyList()
    {
        // Arrange
        await fixture.ResetDbAsync();

        // Act
        var response = await fixture.HttpClient.GetAsync("/api/videos?pageSize=5");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PagedResult<VideoListItemDto>>(content, _serializerOptions);

        Assert.NotNull(result);
        Assert.Empty(result.Items);
        Assert.Null(result.NextPageToken);
    }

    [Fact]
    public async Task GetVideos_WithInvalidVisibility_ReturnsBadRequest()
    {
        // Arrange
        await fixture.ResetDbAsync();

        // Act
        var response = await fixture.HttpClient.GetAsync("/api/videos?visibility=InvalidValue");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetVideos_WithValidVisibilityFilter_ReturnsOk()
    {
        // Arrange
        await fixture.ResetDbAsync();

        // Act
        var response = await fixture.HttpClient.GetAsync("/api/videos?visibility=Public&visibility=Unlisted");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PagedResult<VideoListItemDto>>(content, _serializerOptions);

        Assert.NotNull(result);
        Assert.Empty(result.Items);
        Assert.Null(result.NextPageToken);
    }

    [Fact]
    public async Task GetVideos_CaseInsensitiveVisibility_ReturnsOk()
    {
        // Arrange
        await fixture.ResetDbAsync();

        // Act
        var response = await fixture.HttpClient.GetAsync("/api/videos?visibility=public&visibility=UNLISTED");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PagedResult<VideoListItemDto>>(content, _serializerOptions);

        Assert.NotNull(result);
        Assert.Empty(result.Items);
        Assert.Null(result.NextPageToken);
    }

    [Fact]
    public async Task CopyTemplate_ValidRequest_CallsYoutubeService()
    {
        const string channelId = "Channel-XYZ";
        const string userId = MockAuthenticationExtensions.TestSub;
        // Arrange
        await fixture.ResetDbAsync();

        var sourceVideo = GetSourceVideo();
        var targetVideo = GetTargetVideo();

        using (var scope = fixture.ApiServices.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<YouTubesterDb>();
            var user = User.Create(
                userId,
                MockAuthenticationExtensions.TestEmail,
                MockAuthenticationExtensions.TestName,
                MockAuthenticationExtensions.TestPicture,
                TestFixture.TestingDateTimeOffset);
            await dbContext.Users.AddAsync(user);
            await dbContext.Channels.AddAsync(Channel.Create(channelId, userId, "Channel A",
                targetVideo.UploadsPlaylistId, TestFixture.TestingDateTimeOffset));
            dbContext.Videos.AddRange(sourceVideo, targetVideo);
            await dbContext.SaveChangesAsync();
        }

        fixture.ApiFactory.MockYouTubeIntegration.Setup(x =>
                x.UpdateVideoAsync(targetVideo.VideoId, sourceVideo.Title!, sourceVideo.Description!,
                    sourceVideo.Tags, targetVideo.CategoryId, sourceVideo.DefaultLanguage,
                    sourceVideo.DefaultAudioLanguage,
                    It.IsAny<(double lat, double lng)?>(),
                    targetVideo.LocationDescription, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var request = new CopyVideoTemplateRequest(
            sourceVideo.VideoId,
            targetVideo.VideoId,
            true,
            false,
            true,
            false,
            true,
            null
        );

        var json = JsonSerializer.Serialize(request, _serializerOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await fixture.HttpClient.PostAsync("/api/videos/copy-template", content);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var responseContent = await response.Content.ReadAsStringAsync();
        Assert.False(string.IsNullOrWhiteSpace(responseContent));

        fixture.ApiFactory.MockYouTubeIntegration.Verify(x =>
                x.UpdateVideoAsync(
                    targetVideo.VideoId,
                    sourceVideo.Title!,
                    sourceVideo.Description!,
                    It.Is<IReadOnlyList<string>>(tags =>
                        tags.SequenceEqual(sourceVideo.Tags)),
                    targetVideo.CategoryId,
                    sourceVideo.DefaultLanguage,
                    sourceVideo.DefaultAudioLanguage,
                    It.IsAny<(double lat, double lng)?>(),
                    targetVideo.LocationDescription,
                    It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CopyTemplate_EmptySourceVideoId_ReturnsBadRequest()
    {
        // Arrange
        await fixture.ResetDbAsync();

        var request = new CopyVideoTemplateRequest(
            "",
            "targetVideoId456"
        );

        var json = JsonSerializer.Serialize(request, _serializerOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await fixture.HttpClient.PostAsync("/api/videos/copy-template", content);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var responseContent = await response.Content.ReadAsStringAsync();
        Assert.Contains("SourceVideoId is required", responseContent);
    }

    [Fact]
    public async Task CopyTemplate_EmptyTargetVideoId_ReturnsBadRequest()
    {
        // Arrange
        await fixture.ResetDbAsync();

        var request = new CopyVideoTemplateRequest(
            "sourceVideoId123",
            ""
        );

        var json = JsonSerializer.Serialize(request, _serializerOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await fixture.HttpClient.PostAsync("/api/videos/copy-template", content);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var responseContent = await response.Content.ReadAsStringAsync();
        Assert.Contains("TargetVideoId is required", responseContent);
    }

    [Fact]
    public async Task CopyTemplate_SameSourceAndTargetVideoId_ReturnsBadRequest()
    {
        // Arrange
        await fixture.ResetDbAsync();

        var request = new CopyVideoTemplateRequest(
            "sameVideoId123",
            "sameVideoId123"
        );

        var json = JsonSerializer.Serialize(request, _serializerOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await fixture.HttpClient.PostAsync("/api/videos/copy-template", content);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var responseContent = await response.Content.ReadAsStringAsync();
        Assert.Contains("SourceVideoId and TargetVideoId must be different", responseContent);
    }

    [Fact]
    public async Task GetVideos_WithVideosInDb_ReturnsFilteredAndPagedResults()
    {
        // Arrange
        await fixture.ResetDbAsync();

        var testUploadsPlaylistId = "PLTestUploads123";

        var testChannelID = "testChannelID123";
        var channel = Channel.Create(testChannelID, "testUserId123", "testChannelName123",
            testUploadsPlaylistId, DateTimeOffset.Now);

        fixture.ApiFactory.MockCurrentChannelContext.Setup(x => x.GetRequiredChannelId())
            .Returns(testChannelID);

        var video1 = Video.Create(
            testUploadsPlaylistId,
            "video1",
            "Cooking Tutorial",
            "Learn how to cook",
            TestFixture.TestingDateTimeOffset,
            TimeSpan.FromMinutes(10),
            VideoVisibility.Public,
            new[] { "cooking", "tutorial" },
            "22",
            "en",
            "en",
            null,
            null,
            TestFixture.TestingDateTimeOffset,
            "etag1"
        );

        var video2 = Video.Create(
            testUploadsPlaylistId,
            "video2",
            "Gaming Highlights",
            "Best gaming moments",
            TestFixture.TestingDateTimeOffset.AddDays(1),
            TimeSpan.FromMinutes(15),
            VideoVisibility.Unlisted,
            new[] { "gaming", "highlights" },
            "23",
            "en",
            "en",
            null,
            null,
            TestFixture.TestingDateTimeOffset,
            "etag2"
        );

        var video3 = Video.Create(
            testUploadsPlaylistId,
            "video3",
            "Private Video",
            "This is private",
            TestFixture.TestingDateTimeOffset.AddDays(2),
            TimeSpan.FromMinutes(5),
            VideoVisibility.Private,
            new[] { "private" },
            "24",
            "en",
            "en",
            null,
            null,
            TestFixture.TestingDateTimeOffset,
            "etag3"
        );

        using (var scope = fixture.ApiServices.CreateScope())
        {
            var databaseContext = scope.ServiceProvider.GetRequiredService<YouTubesterDb>();
            databaseContext.Channels.Add(channel);
            databaseContext.Videos.AddRange(video1, video2, video3);
            await databaseContext.SaveChangesAsync();
        }

        // Act - Filter by title
        var titleResponse = await fixture.HttpClient.GetAsync("/api/videos?title=cooking");

        // Assert - Title filter
        Assert.Equal(HttpStatusCode.OK, titleResponse.StatusCode);

        var titleContent = await titleResponse.Content.ReadAsStringAsync();
        var titleResult = JsonSerializer.Deserialize<PagedResult<VideoListItemDto>>(titleContent, _serializerOptions);

        Assert.NotNull(titleResult);
        Assert.Single(titleResult!.Items);
        Assert.Equal("Cooking Tutorial", titleResult.Items.First().Title);

        // Act - Filter by visibility
        var visibilityResponse = await fixture.HttpClient.GetAsync("/api/videos?visibility=Public&visibility=Unlisted");

        // Assert - Visibility filter
        Assert.Equal(HttpStatusCode.OK, visibilityResponse.StatusCode);

        var visibilityContent = await visibilityResponse.Content.ReadAsStringAsync();
        var visibilityResult =
            JsonSerializer.Deserialize<PagedResult<VideoListItemDto>>(visibilityContent, _serializerOptions);

        Assert.NotNull(visibilityResult);
        Assert.Equal(2, visibilityResult!.Items.Count);
        Assert.DoesNotContain(visibilityResult.Items, v => v.Title == "Private Video");

        // Act - Test pagination
        var paginationResponse = await fixture.HttpClient.GetAsync("/api/videos?pageSize=2");

        // Assert - Pagination
        Assert.Equal(HttpStatusCode.OK, paginationResponse.StatusCode);

        var paginationContent = await paginationResponse.Content.ReadAsStringAsync();
        var paginationResult =
            JsonSerializer.Deserialize<PagedResult<VideoListItemDto>>(paginationContent, _serializerOptions);

        Assert.NotNull(paginationResult);
        Assert.Equal(2, paginationResult!.Items.Count);
        Assert.NotNull(paginationResult.NextPageToken);
    }

    [Fact]
    public async Task GetVideos_WithInvalidPageSize_ReturnsBadRequest()
    {
        // Arrange
        await fixture.ResetDbAsync();

        // Act
        var response = await fixture.HttpClient.GetAsync("/api/videos?pageSize=150"); // Exceeds maximum

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var responseContent = await response.Content.ReadAsStringAsync();
        Assert.Contains("error", responseContent);
    }

    [Fact]
    public async Task GetVideos_WithInvalidPageToken_ReturnsBadRequest()
    {
        // Arrange
        await fixture.ResetDbAsync();

        // Act
        var response = await fixture.HttpClient.GetAsync("/api/videos?pageToken=invalid-token");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var responseContent = await response.Content.ReadAsStringAsync();
        Assert.Contains("error", responseContent);
    }

    [Fact]
    public async Task GetVideos_WithNumericVisibilityValues_ReturnsOk()
    {
        // Arrange
        await fixture.ResetDbAsync();

        // Act - Using numeric values for visibility (Public=0, Unlisted=1)
        var response = await fixture.HttpClient.GetAsync("/api/videos?visibility=0&visibility=1");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PagedResult<VideoListItemDto>>(content, _serializerOptions);

        Assert.NotNull(result);
        Assert.Empty(result!.Items); // No videos in DB, but request should be valid
    }

    private static async IAsyncEnumerable<T> CreateAsyncEnumerable<T>(IEnumerable<T> items)
    {
        foreach (var item in items)
        {
            yield return item;
        }

        await Task.CompletedTask;
    }

    private Video GetTargetVideo()
    {
        return Video.Create(
            "ULTestPlaylist456",
            $"target{fixture.Auto.Create<string>()}"[..11],
            "Target Video Title",
            "Target Video Description",
            TestFixture.TestingDateTimeOffset.AddDays(-2),
            TimeSpan.FromMinutes(3),
            VideoVisibility.Private,
            ["target"],
            "23",
            "fr",
            "fr",
            null,
            null,
            TestFixture.TestingDateTimeOffset,
            "etag-target",
            false
        );
    }

    private Video GetSourceVideo()
    {
        return Video.Create(
            "ULTestPlaylist123",
            $"source{fixture.Auto.Create<string>()}"[..11],
            "Source Video Title",
            "Source Video Description",
            TestFixture.TestingDateTimeOffset.AddDays(-1),
            TimeSpan.FromMinutes(5),
            VideoVisibility.Public,
            ["source", "template"],
            "22",
            "en",
            "en",
            new GeoLocation(37.7749, -122.4194),
            "San Francisco, CA",
            TestFixture.TestingDateTimeOffset,
            "etag-source",
            true
        );
    }
}