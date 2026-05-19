using System.Net;
using System.Text;
using Tubester.Application.Contracts;
using Tubester.Application.Contracts.Videos;
using Tubester.Domain;
using Tubester.IntegrationTests.TestHost;
using Xunit;

namespace Tubester.IntegrationTests.Videos;

[Collection(nameof(TestCollection))]
public class VideoSearchTests(TestFixture fixture)
{
    private readonly TestHelpers _helpers = new(fixture.ApiServices);

    [Fact]
    public async Task Search_EmptyDb_ReturnsEmptyList()
    {
        // Arrange
        await fixture.CleanStateAsync();

        var request = new GetVideosRequest { PageSize = 5 };
        var content = TestHelpers.CreateJsonContent(request);

        // Act
        var response = await fixture.HttpClient.PostAsync("/api/videos/search", content);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await TestHelpers.DeserializeAsync<PagedResult<VideoListItemDto>>(response);

        Assert.NotNull(result);
        Assert.Empty(result.Items);
        Assert.Null(result.NextPageToken);
    }

    [Fact]
    public async Task Search_WithInvalidVisibility_ReturnsBadRequest()
    {
        // Arrange
        await fixture.CleanStateAsync();

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
        await fixture.CleanStateAsync();

        var request = new GetVideosRequest { Visibility = [VideoVisibility.Public, VideoVisibility.Unlisted] };
        var content = TestHelpers.CreateJsonContent(request);

        // Act
        var response = await fixture.HttpClient.PostAsync("/api/videos/search", content);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await TestHelpers.DeserializeAsync<PagedResult<VideoListItemDto>>(response);

        Assert.NotNull(result);
        Assert.Empty(result.Items);
        Assert.Null(result.NextPageToken);
    }

    [Fact]
    public async Task Search_CaseInsensitiveVisibility_ReturnsOk()
    {
        // Arrange
        await fixture.CleanStateAsync();

        var request = new GetVideosRequest { Visibility = [VideoVisibility.Public, VideoVisibility.Unlisted] };
        var content = TestHelpers.CreateJsonContent(request);

        // Act
        var response = await fixture.HttpClient.PostAsync("/api/videos/search", content);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await TestHelpers.DeserializeAsync<PagedResult<VideoListItemDto>>(response);

        Assert.NotNull(result);
        Assert.Empty(result.Items);
        Assert.Null(result.NextPageToken);
    }

    [Fact]
    public async Task Search_WithInvalidPageSize_ReturnsBadRequest()
    {
        // Arrange
        await fixture.CleanStateAsync();

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
        await fixture.CleanStateAsync();

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
        await fixture.CleanStateAsync();

        var request = new GetVideosRequest { Visibility = [VideoVisibility.Public, VideoVisibility.Unlisted] };

        // Act
        var response = await fixture.HttpClient.PostAsync(
            "/api/videos/search",
            TestHelpers.CreateJsonContent(request));

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await TestHelpers.DeserializeAsync<PagedResult<VideoListItemDto>>(response);

        Assert.NotNull(result);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task Search_WithVideosInDb_ReturnsFilteredAndPagedResults()
    {
        // Arrange
        await fixture.CleanStateAsync();

        var video1 = TestHelpers.GetTargetVideo("cooking-tutorial");
        var video2 = TestHelpers.GetTargetVideo("gaming-video", VideoVisibility.Unlisted);
        var video3 = TestHelpers.GetTargetVideo("private-video", VideoVisibility.Private);

        await _helpers.SeedTestDataAsync(new TestDataOptions
        {
            Videos = [video1, video2, video3]
        });

        // Act - Filter by title
        var titleRequest = new GetVideosRequest { Title = "cooking" };

        var response = await fixture.HttpClient.PostAsync(
            "/api/videos/search",
            TestHelpers.CreateJsonContent(titleRequest));

        // Assert - Title filter
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var pagedResult = await TestHelpers.DeserializeAsync<PagedResult<VideoListItemDto>>(response);

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
        var visibilityResult = await TestHelpers.DeserializeAsync<PagedResult<VideoListItemDto>>(visibilityResponse);

        Assert.Equal(2, visibilityResult.Items.Count);
        Assert.DoesNotContain(visibilityResult.Items, v => v.Title == video3.Title);

        // Act - Test pagination
        var paginationRequest = new GetVideosRequest { PageSize = 2 };

        var paginationResponse = await fixture.HttpClient.PostAsync(
            "/api/videos/search",
            TestHelpers.CreateJsonContent(paginationRequest));

        // Assert - Pagination
        Assert.Equal(HttpStatusCode.OK, paginationResponse.StatusCode);
        var paginationResult = await TestHelpers.DeserializeAsync<PagedResult<VideoListItemDto>>(paginationResponse);

        Assert.NotNull(paginationResult);
        Assert.Equal(2, paginationResult.Items.Count);
        Assert.NotNull(paginationResult.NextPageToken);
    }
}