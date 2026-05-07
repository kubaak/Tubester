using System.Net;
using Moq;
using Tubester.Application.Contracts.Videos;
using Tubester.IntegrationTests.TestHost;
using Xunit;

namespace Tubester.IntegrationTests;

[Collection(nameof(TestCollection))]
public class VideosTests(TestFixture fixture)
{
    private readonly TestHelpers _helpers = new(fixture.ApiServices);

    [Fact]
    public async Task SaveDraft_ValidRequest_SavesMetadataToDbWithoutCallingYouTube()
    {
        // Arrange
        await fixture.CleanStateAsync();
        
        var targetVideo = TestHelpers.GetTargetVideo();
        await _helpers.SeedTestDataAsync(new TestDataOptions { Videos = [targetVideo] });

        const string newTitle = "Draft Title";
        const string newDescription = "Draft Description";
        var newTags = new[] { "draft-tag-one", "draft-tag-two" };

        var request = new SaveVideoDraftRequest(
            TestConstants.TargetVideoId,
            newTitle,
            newDescription,
            newTags,
            null
        );

        // Act
        var response = await fixture.HttpClient.PostAsync("/api/videos/save-draft",
            TestHelpers.CreateJsonContent(request));

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var videoDetails = await TestHelpers.DeserializeAsync<VideoDetailsDto>(response);


        TestHelpers.SetVideoProperties(targetVideo, newTitle, newDescription, newTags);
        TestHelpers.AssertVideoDetails(videoDetails, targetVideo);
        await _helpers.AssertVideoAsync(targetVideo);

        fixture.ApiFactory.MockYouTubeIntegration.Verify(youTubeIntegration =>
                youTubeIntegration.UpdateVideoAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<IReadOnlyList<string>>(),
                    It.IsAny<string?>(),
                    It.IsAny<string?>(),
                    It.IsAny<string?>(),
                    It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SaveDraft_VideoDoesNotExist_ReturnsNotFound()
    {
        // Arrange
        await fixture.CleanStateAsync();
        await _helpers.SeedTestDataAsync();

        var request = new UpdateVideoMetadataRequest(
            "does-not-exist",
            "Some Title",
            "Some Description",
            ["tag"],
            null
        );

        // Act
        var response = await fixture.HttpClient.PostAsync(
            "/api/videos/save-draft",
            TestHelpers.CreateJsonContent(request));

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SaveDraft_EmptyVideoId_ReturnsBadRequest()
    {
        // Arrange
        var request = new UpdateVideoMetadataRequest(
            "",
            "Some Title",
            "Some Description",
            ["tag"],
            null
        );

        // Act
        var response = await fixture.HttpClient.PostAsync(
            "/api/videos/save-draft",
            TestHelpers.CreateJsonContent(request));

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var responseContent = await response.Content.ReadAsStringAsync();
        Assert.Contains("VideoId is required", responseContent);
    }

    [Fact]
    public async Task SaveDraft_EmptyTitle_ReturnsBadRequest()
    {
        // Arrange
        var request = new UpdateVideoMetadataRequest(
            "someVideoId",
            "",
            "Some Description",
            ["tag"],
            null
        );

        // Act
        var response = await fixture.HttpClient.PostAsync(
            "/api/videos/save-draft",
            TestHelpers.CreateJsonContent(request));

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var responseContent = await response.Content.ReadAsStringAsync();
        Assert.Contains("Title is required", responseContent);
    }

    [Fact]
    public async Task UpdateVideo_WithPlaylistIds_UpdatesVideoPlaylistMemberships()
    {
        // Arrange
        await fixture.CleanStateAsync();
        
        var targetVideo = TestHelpers.GetTargetVideo();
        var playlist1 = TestHelpers.GetPlaylist("PL1");
        var playlist2 = TestHelpers.GetPlaylist("PL2");
        await _helpers.SeedTestDataAsync(new TestDataOptions
        {
            Videos = [targetVideo],
            Playlists = [playlist1, playlist2]
        });

        const string newTitle = "Updated Title With Playlists";
        const string newDescription = "Updated Description";
        var newTags = new[] { "updated", "playlist-test" };

        var request = new UpdateVideoMetadataRequest(
            targetVideo.VideoId,
            newTitle,
            newDescription,
            newTags,
            [playlist1.PlaylistId, playlist2.PlaylistId]
        );

        fixture.ApiFactory.MockYouTubeIntegration
            .Setup(youTubeIntegration => youTubeIntegration.UpdateVideoAsync(
                targetVideo.VideoId,
                newTitle,
                newDescription,
                It.Is<IReadOnlyList<string>>(tags => tags.SequenceEqual(newTags)),
                targetVideo.CategoryId,
                targetVideo.DefaultLanguage,
                targetVideo.DefaultAudioLanguage,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        fixture.ApiFactory.MockYouTubeIntegration
            .Setup(youTubeIntegration => youTubeIntegration.AddVideoToPlaylistAsync(
                It.IsAny<string>(),
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


        TestHelpers.SetProperty(targetVideo, nameof(targetVideo.UpdatedAt), TestFixture.TestingDateTimeOffset);
        TestHelpers.SetProperty(targetVideo, nameof(targetVideo.Title), newTitle);
        TestHelpers.SetProperty(targetVideo, nameof(targetVideo.Description), newDescription);
        TestHelpers.SetProperty(targetVideo, nameof(targetVideo.Tags), newTags);
        TestHelpers.SetProperty<string>(targetVideo, nameof(targetVideo.ETag), null); //Etag will be reset
        var expectedPlaylistDtos = new PlaylistDto[] {
            new() { Id = playlist1.PlaylistId, Name = playlist1.Title },
            new() { Id = playlist2.PlaylistId, Name = playlist2.Title }

        };
        TestHelpers.SetVideoProperties(targetVideo, newTitle, newDescription, newTags);
        TestHelpers.AssertVideoDetails(videoDetails, targetVideo, expectedPlaylistDtos);
        await _helpers.AssertVideoAsync(targetVideo);
        await _helpers.AssertVideoPlaylistsAsync(targetVideo.VideoId, playlist1.PlaylistId, playlist2.PlaylistId);

        fixture.ApiFactory.MockYouTubeIntegration.Verify(youTubeIntegration =>
                youTubeIntegration.UpdateVideoAsync(
                    targetVideo.VideoId,
                    newTitle,
                    newDescription,
                    It.Is<IReadOnlyList<string>>(tags => tags.SequenceEqual(newTags)),
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

    [Fact]
    public async Task SaveDraft_WithPlaylistIds_UpdatesVideoPlaylistMembershipsWithoutYouTubeCall()
    {
        // Arrange
        await fixture.CleanStateAsync();
        
        var targetVideo = TestHelpers.GetTargetVideo();
        var playlist1 = TestHelpers.GetPlaylist("PL1");
        var playlist2 = TestHelpers.GetPlaylist("PL2");
        await _helpers.SeedTestDataAsync(new TestDataOptions
        {
            Videos = [targetVideo],
            Playlists = [playlist1, playlist2]
        });
        const string newTitle = "Draft Title With Playlists";
        const string newDescription = "Draft Description";
        var newTags = new[] { "draft", "playlist" };

        var request = new UpdateVideoMetadataRequest(
            targetVideo.VideoId,
            newTitle,
            newDescription,
            newTags,
            [playlist1.PlaylistId, playlist2.PlaylistId]
        );

        // Act
        var response = await fixture.HttpClient.PostAsync(
            "/api/videos/save-draft",
            TestHelpers.CreateJsonContent(request));

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var videoDetails = await TestHelpers.DeserializeAsync<VideoDetailsDto>(response);

        TestHelpers.SetProperty(targetVideo, nameof(targetVideo.UpdatedAt), TestFixture.TestingDateTimeOffset);
        TestHelpers.SetProperty(targetVideo, nameof(targetVideo.Title), newTitle);
        TestHelpers.SetProperty(targetVideo, nameof(targetVideo.Description), newDescription);
        TestHelpers.SetProperty(targetVideo, nameof(targetVideo.Tags), newTags);
        TestHelpers.SetProperty<string>(targetVideo, nameof(targetVideo.ETag), null); //Etag will be reset
        var expectedPlaylistDtos = new PlaylistDto[] {
            new() { Id = playlist1.PlaylistId, Name = playlist1.Title },
            new() { Id = playlist2.PlaylistId, Name = playlist2.Title }

        };
        TestHelpers.SetVideoProperties(targetVideo, newTitle, newDescription, newTags);
        TestHelpers.AssertVideoDetails(videoDetails, targetVideo, expectedPlaylistDtos);
        await _helpers.AssertVideoAsync(targetVideo);
        await _helpers.AssertVideoPlaylistsAsync(targetVideo.VideoId, playlist1.PlaylistId, playlist2.PlaylistId);

        fixture.ApiFactory.MockYouTubeIntegration.Verify(youTubeIntegration =>
                youTubeIntegration.UpdateVideoAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<IReadOnlyList<string>>(),
                    It.IsAny<string?>(),
                    It.IsAny<string?>(),
                    It.IsAny<string?>(),
                    It.IsAny<CancellationToken>()),
            Times.Never);
    }
}