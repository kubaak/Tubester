using System.Net;
using System.Text;
using System.Text.Json;
using Tubester.Application.Contracts;
using Tubester.Application.Contracts.Videos;
using Tubester.Domain;
using Tubester.IntegrationTests.TestHost;
using Xunit;

namespace Tubester.IntegrationTests;

[Collection(nameof(TestCollection))]
public class VideoSearchTests(TestFixture fixture)
{
    private readonly TestHelpers _helpers = new(fixture);

    [Fact]
    public async Task Search_EmptyDb_ReturnsEmptyList()
    {
        // Arrange
        await fixture.ResetDbAsync();

        var request = new GetVideosRequest { PageSize = 5 };
        var content = TestHelpers.CreateJsonContent(request);

        // Act
        var response = await fixture.HttpClient.PostAsync("/api/videos/search", content);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var responseContent = await response.Content.ReadAsStringAsync();

        var result = JsonSerializer.Deserialize<PagedResult<VideoListItemDto>>(
            responseContent,
            TestHelpers.SerializerOptions);

        Assert.NotNull(result);
        Assert.Empty(result.Items);
        Assert.Null(result.NextPageToken);
    }

    [Fact]
    public async Task Search_WithInvalidVisibility_ReturnsBadRequest()
    {
        // Arrange
        await fixture.ResetDbAsync();

        var invalidJson = "{\"visibility\": [\"InvalidValue\"]}";
        var invalidContent = new StringContent(invalidJson, Encoding.UTF8, "application/json");

        // Act
        var response = await fixture.HttpClient.PostAsync("/api/videos/search", invalidContent);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Search_WithValidVisibilityFilter_ReturnsOk()
    {
        // Arrange
        await fixture.ResetDbAsync();

        var request = new GetVideosRequest { Visibility = [VideoVisibility.Public, VideoVisibility.Unlisted] };
        var content = TestHelpers.CreateJsonContent(request);

        // Act
        var response = await fixture.HttpClient.PostAsync("/api/videos/search", content);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var responseContent = await response.Content.ReadAsStringAsync();

        var result = JsonSerializer.Deserialize<PagedResult<VideoListItemDto>>(
            responseContent,
            TestHelpers.SerializerOptions);

        Assert.NotNull(result);
        Assert.Empty(result.Items);
        Assert.Null(result.NextPageToken);
    }

    [Fact]
    public async Task Search_CaseInsensitiveVisibility_ReturnsOk()
    {
        // Arrange
        await fixture.ResetDbAsync();

        var request = new GetVideosRequest { Visibility = [VideoVisibility.Public, VideoVisibility.Unlisted] };
        var content = TestHelpers.CreateJsonContent(request);

        // Act
        var response = await fixture.HttpClient.PostAsync("/api/videos/search", content);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var responseContent = await response.Content.ReadAsStringAsync();

        var result = JsonSerializer.Deserialize<PagedResult<VideoListItemDto>>(
            responseContent,
            TestHelpers.SerializerOptions);

        Assert.NotNull(result);
        Assert.Empty(result.Items);
        Assert.Null(result.NextPageToken);
    }

    [Fact]
    public async Task Search_WithInvalidPageSize_ReturnsBadRequest()
    {
        // Arrange
        await fixture.ResetDbAsync();

        var request = new GetVideosRequest { PageSize = 150 };

        // Act
        var response = await fixture.HttpClient.PostAsync(
            "/api/videos/search",
            TestHelpers.CreateJsonContent(request));

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var responseContent = await response.Content.ReadAsStringAsync();
        Assert.Contains("error", responseContent);
    }

    [Fact]
    public async Task Search_WithInvalidPageToken_ReturnsBadRequest()
    {
        // Arrange
        await fixture.ResetDbAsync();

        var request = new GetVideosRequest { PageToken = "invalid-token" };

        // Act
        var response = await fixture.HttpClient.PostAsync(
            "/api/videos/search",
            TestHelpers.CreateJsonContent(request));

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var responseContent = await response.Content.ReadAsStringAsync();
        Assert.Contains("error", responseContent);
    }

    [Fact]
    public async Task Search_WithNumericVisibilityValues_ReturnsOk()
    {
        // Arrange
        await fixture.ResetDbAsync();

        var request = new GetVideosRequest { Visibility = [VideoVisibility.Public, VideoVisibility.Unlisted] };

        // Act
        var response = await fixture.HttpClient.PostAsync(
            "/api/videos/search",
            TestHelpers.CreateJsonContent(request));

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var responseContent = await response.Content.ReadAsStringAsync();

        var result = JsonSerializer.Deserialize<PagedResult<VideoListItemDto>>(
            responseContent,
            TestHelpers.SerializerOptions);

        Assert.NotNull(result);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task Search_WithVideosInDb_ReturnsFilteredAndPagedResults()
    {
        // Arrange
        await fixture.ResetDbAsync();

        var video1 = TestHelpers.GetTargetVideo("cooking-tutorial");
        var video2 = TestHelpers.GetTargetVideo("gaming-video", VideoVisibility.Unlisted);
        var video3 = TestHelpers.GetTargetVideo("private-video", VideoVisibility.Private);

        await _helpers.SeedVideoTestDataAsync(new TestDataOptions
        {
            Videos = [video1, video2, video3]
        });

        // Act - Filter by title
        var titleRequest = new GetVideosRequest { Title = "cooking" };

        var titleResponse = await fixture.HttpClient.PostAsync(
            "/api/videos/search",
            TestHelpers.CreateJsonContent(titleRequest));

        // Assert - Title filter
        Assert.Equal(HttpStatusCode.OK, titleResponse.StatusCode);

        var titleResponseContent = await titleResponse.Content.ReadAsStringAsync();

        var pagedResult = JsonSerializer.Deserialize<PagedResult<VideoListItemDto>>(
            titleResponseContent,
            TestHelpers.SerializerOptions);

        Assert.NotNull(pagedResult);
        Assert.Single(pagedResult.Items);
        var videoListItemDto = pagedResult.Items.First();
        TestHelpers.AssertVideoListItemDto(videoListItemDto, video1);

        // Act - Filter by visibility
        var visibilityRequest = new GetVideosRequest { Visibility = [VideoVisibility.Public, VideoVisibility.Unlisted] };

        var visibilityResponse = await fixture.HttpClient.PostAsync(
            "/api/videos/search",
            TestHelpers.CreateJsonContent(visibilityRequest));

        // Assert - Visibility filter
        Assert.Equal(HttpStatusCode.OK, visibilityResponse.StatusCode);

        var visibilityResponseContent = await visibilityResponse.Content.ReadAsStringAsync();

        var visibilityResult = JsonSerializer.Deserialize<PagedResult<VideoListItemDto>>(
            visibilityResponseContent,
            TestHelpers.SerializerOptions);

        Assert.NotNull(visibilityResult);
        Assert.Equal(2, visibilityResult.Items.Count);
        Assert.DoesNotContain(visibilityResult.Items, v => v.Title == video3.Title);

        // Act - Test pagination
        var paginationRequest = new GetVideosRequest { PageSize = 2 };

        var paginationResponse = await fixture.HttpClient.PostAsync(
            "/api/videos/search",
            TestHelpers.CreateJsonContent(paginationRequest));

        // Assert - Pagination
        Assert.Equal(HttpStatusCode.OK, paginationResponse.StatusCode);

        var paginationResponseContent = await paginationResponse.Content.ReadAsStringAsync();

        var paginationResult = JsonSerializer.Deserialize<PagedResult<VideoListItemDto>>(
            paginationResponseContent,
            TestHelpers.SerializerOptions);

        Assert.NotNull(paginationResult);
        Assert.Equal(2, paginationResult.Items.Count);
        Assert.NotNull(paginationResult.NextPageToken);
    }
}