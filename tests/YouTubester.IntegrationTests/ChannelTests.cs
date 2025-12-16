using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;
using YouTubester.Abstractions.Channels;
using YouTubester.Abstractions.Users;
using YouTubester.Application.Channels;
using YouTubester.Domain;
using YouTubester.Integration.Dtos;
using YouTubester.IntegrationTests.TestHost;
using YouTubester.Persistence;
using YouTubester.Persistence.Users;

namespace YouTubester.IntegrationTests;

[Collection(nameof(TestCollection))]
public sealed class ChannelTests(TestFixture fixture)
{
    [Fact]
    public async Task PullChannel_Creates_Then_Updates_Channel_In_Database()
    {
        // Arrange
        await fixture.ResetDbAsync();

        const string channelId = "UC1234567890KITTENS";
        const string uploadsIdV1 = "PL-UPLOADS-V1";
        const string uploadsIdV2 = "PL-UPLOADS-V2";
        const string nameV1 = "Cute Kittens";
        const string nameV2 = "Cuter Kittens";
        const string etagV1 = "etag-v1";
        const string etagV2 = "etag-v2";

        // First call returns initial snapshot, second call returns changed data
        fixture.ApiFactory.MockYouTubeIntegration
            .SetupSequence(x => x.GetChannelAsync(channelId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChannelDto(channelId, nameV1, uploadsIdV1, etagV1))
            .ReturnsAsync(new ChannelDto(channelId, nameV2, uploadsIdV2, etagV2));

        // --- Act #1: create ---
        var createResp = await fixture.HttpClient.PostAsync($"/api/channels/pull/{channelId}", null);
        Assert.Equal(HttpStatusCode.OK, createResp.StatusCode);

        // Assert DB after creation
        using (var scope = fixture.ApiServices.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<YouTubesterDb>();
            var ch = await db.Channels.AsNoTracking().SingleOrDefaultAsync(c => c.ChannelId == channelId);

            Assert.NotNull(ch);
            Assert.Equal(nameV1, ch!.Name);
            Assert.Equal(uploadsIdV1, ch.UploadsPlaylistId);
            Assert.Equal(etagV1, ch.ETag);
            Assert.Null(ch.LastUploadsCutoff); // not set by pull
            Assert.True(ch.UpdatedAt > TestFixture.TestingDateTimeOffset); // sanity check it's set
        }

        // --- Act #2: update (different name, uploads, etag) ---
        var updateResp = await fixture.HttpClient.PostAsync($"/api/channels/pull/{channelId}", null);
        Assert.Equal(HttpStatusCode.OK, updateResp.StatusCode);

        // Assert DB after update
        using (var scope = fixture.ApiServices.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<YouTubesterDb>();
            var ch = await db.Channels.AsNoTracking().SingleOrDefaultAsync(c => c.ChannelId == channelId);

            Assert.NotNull(ch);
            Assert.Equal(nameV2, ch!.Name);
            Assert.Equal(uploadsIdV2, ch.UploadsPlaylistId);
            Assert.Equal(etagV2, ch.ETag);
        }

        // Verify the integration was called twice with the same input channel id
        fixture.ApiFactory.MockYouTubeIntegration.Verify(
            m => m.GetChannelAsync(channelId,
                It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task GetAvailableChannels_Returns_Channels_From_YouTubeIntegration()
    {
        // Arrange
        await fixture.ResetDbAsync();

        // Ensure the current user has valid Google tokens so the application layer
        // does not short-circuit before calling YouTubeIntegration.
        using (var scope = fixture.ApiServices.CreateScope())
        {
            var databaseContext = scope.ServiceProvider.GetRequiredService<YouTubesterDb>();
            var user = User.Create(MockAuthenticationExtensions.TestSub, MockAuthenticationExtensions.TestEmail,
                MockAuthenticationExtensions.TestName, MockAuthenticationExtensions.TestPicture, DateTimeOffset.UtcNow);
            databaseContext.Users.Add(user);
            await databaseContext.SaveChangesAsync();
            var userTokens = UserToken.Create(
                MockAuthenticationExtensions.TestSub,
                "refresh-token",
                "access-token",
                DateTimeOffset.UtcNow.AddHours(1));
            databaseContext.UserTokens.Add(userTokens);
            await databaseContext.SaveChangesAsync();
        }

        var remoteChannels = new List<ChannelDto>
        {
            new(
                "UCChannelId1",
                "First Channel",
                "UploadsPlaylistId1",
                "etag-1"),
            new(
                "UCChannelId2",
                "Second Channel",
                "UploadsPlaylistId2",
                "etag-2")
        };

        fixture.ApiFactory.MockYouTubeIntegration
            .Setup(x => x.GetUserChannelsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(remoteChannels);

        // Act
        var response = await fixture.HttpClient.GetAsync("/api/channels/available");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var responseContent = await response.Content.ReadAsStringAsync();
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var deserialized = JsonSerializer.Deserialize<List<ChannelDto>>(responseContent, options);

        Assert.NotNull(deserialized);
        Assert.Equal(2, deserialized!.Count);
        Assert.Equal("UCChannelId1", deserialized[0].Id);
        Assert.Equal("First Channel", deserialized[0].Name);
        Assert.Equal("UCChannelId2", deserialized[1].Id);
        Assert.Equal("UploadsPlaylistId2", deserialized[1].UploadsPlaylistId);

        fixture.ApiFactory.MockYouTubeIntegration.Verify(
            x => x.GetUserChannelsAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetUserChannels_Returns_Channels_For_Current_User()
    {
        // Arrange
        await fixture.ResetDbAsync();

        using (var scope = fixture.ApiServices.CreateScope())
        {
            var databaseContext = scope.ServiceProvider.GetRequiredService<YouTubesterDb>();

            var user = User.Create(
                MockAuthenticationExtensions.TestSub,
                MockAuthenticationExtensions.TestEmail,
                MockAuthenticationExtensions.TestName,
                MockAuthenticationExtensions.TestPicture,
                TestFixture.TestingDateTimeOffset);

            databaseContext.Users.Add(user);

            var firstChannel = Channel.Create(
                "UCUserChannel1",
                MockAuthenticationExtensions.TestSub,
                "First User Channel",
                "UploadsPlaylistId1",
                TestFixture.TestingDateTimeOffset);

            var secondChannel = Channel.Create(
                "UCUserChannel2",
                MockAuthenticationExtensions.TestSub,
                "Second User Channel",
                "UploadsPlaylistId2",
                TestFixture.TestingDateTimeOffset);

            databaseContext.Channels.Add(firstChannel);
            databaseContext.Channels.Add(secondChannel);

            await databaseContext.SaveChangesAsync();
        }

        // Act
        var response = await fixture.HttpClient.GetAsync("/api/channels");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var responseContent = await response.Content.ReadAsStringAsync();
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var deserialized = JsonSerializer.Deserialize<List<UserChannelDto>>(responseContent, options);

        Assert.NotNull(deserialized);
        Assert.Equal(2, deserialized!.Count);

        var firstResultChannel = deserialized.Single(channel => channel.Id == "UCUserChannel1");
        Assert.Equal("First User Channel", firstResultChannel.Title);
        Assert.Equal(MockAuthenticationExtensions.TestPicture, firstResultChannel.Picture);

        var secondResultChannel = deserialized.Single(channel => channel.Id == "UCUserChannel2");
        Assert.Equal("Second User Channel", secondResultChannel.Title);
        Assert.Equal(MockAuthenticationExtensions.TestPicture, secondResultChannel.Picture);
    }

    // [Fact] todo keep the test after storing the token for comment scan
    // public async Task SyncCurrentChannels_Enqueues_Background_Job_And_Returns_Accepted()
    // {
    //     // Arrange
    //     await fixture.ResetDbAsync();
    //
    //     // Act
    //     var response = await fixture.HttpClient.PostAsync("/api/channels/sync/current", null);
    //
    //     // Assert HTTP response
    //     Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    //
    //     var responseContent = await response.Content.ReadAsStringAsync();
    //     using var jsonDocument = JsonDocument.Parse(responseContent);
    //     var rootElement = jsonDocument.RootElement;
    //     Assert.True(rootElement.TryGetProperty("status", out var statusProperty));
    //     Assert.Equal("scheduled", statusProperty.GetString());
    //
    //     // Assert background job was enqueued for the current user
    //     var capturedJobs = fixture.CapturingJobClient.GetEnqueued<IChannelSyncService>();
    //     Assert.Single(capturedJobs);
    //
    //     var capturedJob = capturedJobs.Single();
    //     Assert.Equal(nameof(IChannelSyncService.SyncChannelAsync), capturedJob.Job.Method.Name);
    //     Assert.Equal(2, capturedJob.Job.Args.Count);
    //     Assert.Equal(MockAuthenticationExtensions.TestSub, capturedJob.Job.Args[0]);
    // }

    [Fact]
    public async Task Sync_WithDummyChannelAndMockedYouTubeData_UpdatesDatabaseCorrectly()
    {
        // Arrange
        await fixture.ResetDbAsync();

        const string testChannelId = "UCTestChannel123456789";
        const string testChannelName = "TestChannelName";
        const string testUploadsPlaylistId = "PLTestUploads123456789";
        const string userId = MockAuthenticationExtensions.TestSub;

        // Insert dummy user and channel into database
        var dummyUser = User.Create(
            userId,
            MockAuthenticationExtensions.TestEmail,
            MockAuthenticationExtensions.TestName,
            MockAuthenticationExtensions.TestPicture,
            TestFixture.TestingDateTimeOffset);

        var dummyChannel = Channel.Create(
            testChannelId,
            userId,
            testChannelName,
            testUploadsPlaylistId,
            TestFixture.TestingDateTimeOffset
        );

        using (var scope = fixture.ApiServices.CreateScope())
        {
            var databaseContext = scope.ServiceProvider.GetRequiredService<YouTubesterDb>();
            databaseContext.Users.Add(dummyUser);
            databaseContext.Channels.Add(dummyChannel);
            await databaseContext.SaveChangesAsync();
        }

        // Create mock video DTOs
        var mockVideos = new List<VideoDto>
        {
            new(
                "video123",
                "Test Video 1",
                "Test Description 1",
                ["tag1", "tag2"],
                TimeSpan.FromMinutes(5),
                "public",
                false,
                new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero),
                "22",
                "en",
                "en",
                null,
                null,
                "etag-video123",
                null
            ),
            new(
                "video456",
                "Test Video 2",
                "Test Description 2",
                ["tag3", "tag4"],
                TimeSpan.FromMinutes(10),
                "unlisted",
                false,
                new DateTimeOffset(2024, 1, 2, 12, 0, 0, TimeSpan.Zero),
                "23",
                "en",
                "en",
                null,
                null,
                "etag-video456",
                null
            )
        };

        var mockPlaylistData = new List<PlaylistDto>
        {
            new("playlist123", "Test Playlist 1", "etag-playlist123"),
            new("playlist456", "Test Playlist 2", "etag-playlist456")
        };

        var mockPlaylistVideoIds = new Dictionary<string, List<string>>
        {
            ["playlist123"] = ["video123", "video456"], ["playlist456"] = ["video456"]
        };

        // Setup MockYouTubeIntegration
        fixture.ApiFactory.MockYouTubeIntegration
            .Setup(x => x.GetAllVideosAsync(testUploadsPlaylistId,
                It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncEnumerable(mockVideos));

        fixture.ApiFactory.MockYouTubeIntegration
            .Setup(x => x.GetPlaylistsAsync(testChannelId,
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncEnumerable(mockPlaylistData));

        fixture.ApiFactory.MockYouTubeIntegration
            .Setup(x => x.GetPlaylistVideoIdsAsync("playlist123",
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncEnumerable(mockPlaylistVideoIds["playlist123"]));

        fixture.ApiFactory.MockYouTubeIntegration
            .Setup(x => x.GetPlaylistVideoIdsAsync("playlist456",
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncEnumerable(mockPlaylistVideoIds["playlist456"]));

        fixture.ApiFactory.MockYouTubeIntegration
            .Setup(x => x.GetVideosAsync(It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockVideos.ToList().AsReadOnly());

        fixture.ApiFactory.MockCurrentChannelContext.Setup(x => x.GetRequiredChannelId())
            .Returns(testChannelId);

        // Act
        var response = await fixture.HttpClient.PostAsync($"/api/channels/sync/current", null);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var responseContent = await response.Content.ReadAsStringAsync();
        var syncResult = JsonSerializer.Deserialize<ChannelSyncResult>(responseContent,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        Assert.NotNull(syncResult);
        Assert.Equal(2, syncResult.VideosInserted);
        Assert.Equal(0, syncResult.VideosUpdated);
        Assert.Equal(2, syncResult.PlaylistsInserted);
        Assert.Equal(0, syncResult.PlaylistsUpdated);
        Assert.Equal(3, syncResult.MembershipsAdded);

        // Assert database state
        using var verificationScope = fixture.ApiServices.CreateScope();
        var verificationDatabaseContext = verificationScope.ServiceProvider.GetRequiredService<YouTubesterDb>();

        // Verify videos were created
        var createdVideos = await verificationDatabaseContext.Videos
            .AsNoTracking()
            .Where(v => v.UploadsPlaylistId == testUploadsPlaylistId)
            .OrderBy(v => v.VideoId)
            .ToListAsync();

        Assert.Equal(2, createdVideos.Count);

        var firstVideo = createdVideos.First(v => v.VideoId == "video123");
        Assert.Equal("Test Video 1", firstVideo.Title);
        Assert.Equal(TimeSpan.FromMinutes(5), firstVideo.Duration);
        Assert.Equal(VideoVisibility.Public, firstVideo.Visibility);
        Assert.Equal(new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero), firstVideo.PublishedAt);

        var secondVideo = createdVideos.First(v => v.VideoId == "video456");
        Assert.Equal("Test Video 2", secondVideo.Title);
        Assert.Equal(TimeSpan.FromMinutes(10), secondVideo.Duration);
        Assert.Equal(VideoVisibility.Unlisted, secondVideo.Visibility);
        Assert.Equal(new DateTimeOffset(2024, 1, 2, 12, 0, 0, TimeSpan.Zero), secondVideo.PublishedAt);

        // Verify playlists were created
        var createdPlaylists = await verificationDatabaseContext.Playlists
            .AsNoTracking()
            .Where(p => p.ChannelId == testChannelId)
            .OrderBy(p => p.PlaylistId)
            .ToListAsync();

        Assert.Equal(2, createdPlaylists.Count);
        Assert.Equal("playlist123", createdPlaylists[0].PlaylistId);
        Assert.Equal("Test Playlist 1", createdPlaylists[0].Title);
        Assert.Equal("playlist456", createdPlaylists[1].PlaylistId);
        Assert.Equal("Test Playlist 2", createdPlaylists[1].Title);

        // Verify playlist memberships were created
        var memberships = await verificationDatabaseContext.VideoPlaylists
            .AsNoTracking()
            .OrderBy(vp => vp.PlaylistId)
            .ThenBy(vp => vp.VideoId)
            .ToListAsync();

        Assert.Equal(3, memberships.Count);
        Assert.Contains(memberships, vp => vp.PlaylistId == "playlist123" && vp.VideoId == "video123");
        Assert.Contains(memberships, vp => vp.PlaylistId == "playlist123" && vp.VideoId == "video456");
        Assert.Contains(memberships, vp => vp.PlaylistId == "playlist456" && vp.VideoId == "video456");

        // Verify channel uploads cutoff was updated
        var updatedChannel = await verificationDatabaseContext.Channels
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.ChannelId == testChannelId);

        Assert.NotNull(updatedChannel);
        Assert.NotNull(updatedChannel.LastUploadsCutoff);
        Assert.Equal(new DateTimeOffset(2024, 1, 2, 12, 0, 0, TimeSpan.Zero), updatedChannel.LastUploadsCutoff);
    }

    [Fact]
    public async Task Sync_CalledTwice_IsIdempotentAndUpdatesExistingData()
    {
        // Arrange
        await fixture.ResetDbAsync();

        const string testChannelId = "UCTestChannel987654321";
        const string testChannelName = "TestChannelIdempotent";
        const string testUploadsPlaylistId = "PLTestUploads987654321";
        const string userId = MockAuthenticationExtensions.TestSub;

        // Insert dummy user and channel into database
        var dummyUser = User.Create(
            userId,
            MockAuthenticationExtensions.TestEmail,
            MockAuthenticationExtensions.TestName,
            MockAuthenticationExtensions.TestPicture,
            TestFixture.TestingDateTimeOffset);

        var dummyChannel = Channel.Create(
            testChannelId,
            userId,
            testChannelName,
            testUploadsPlaylistId,
            TestFixture.TestingDateTimeOffset
        );

        using (var scope = fixture.ApiServices.CreateScope())
        {
            var databaseContext = scope.ServiceProvider.GetRequiredService<YouTubesterDb>();
            databaseContext.Users.Add(dummyUser);
            databaseContext.Channels.Add(dummyChannel);
            await databaseContext.SaveChangesAsync();
        }

        // Create mock video DTOs
        var mockVideosFirstCall = new List<VideoDto>
        {
            new(
                "video789",
                "Original Title",
                "Original Description",
                ["tag1"],
                TimeSpan.FromMinutes(3),
                "public",
                false,
                new DateTimeOffset(2024, 2, 1, 12, 0, 0, TimeSpan.Zero),
                "22",
                "en",
                "en",
                null,
                null,
                "etag-video789-v1",
                null
            )
        };

        var mockVideosSecondCall = new List<VideoDto>
        {
            new(
                "video789",
                "Updated Title",
                "Updated Description",
                ["tag1"],
                TimeSpan.FromMinutes(3),
                "public",
                false,
                new DateTimeOffset(2024, 2, 1, 12, 0, 0, TimeSpan.Zero),
                "22",
                "en",
                "en",
                null,
                null,
                "etag-video789-v2",
                null
            )
        };

        var mockPlaylistData = new List<PlaylistDto> { new("playlist789", "Test Playlist", "etag-video789-v1") };

        var mockPlaylistVideoIds = new Dictionary<string, List<string>> { ["playlist789"] = ["video789"] };

        // Setup MockYouTubeIntegration for first call
        fixture.ApiFactory.MockYouTubeIntegration
            .SetupSequence(x =>
                x.GetAllVideosAsync(testUploadsPlaylistId,
                    It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncEnumerable(mockVideosFirstCall))
            .Returns(CreateAsyncEnumerable(mockVideosSecondCall));

        fixture.ApiFactory.MockYouTubeIntegration
            .Setup(x => x.GetPlaylistsAsync(testChannelId,
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncEnumerable(mockPlaylistData));

        fixture.ApiFactory.MockYouTubeIntegration
            .Setup(x => x.GetPlaylistVideoIdsAsync("playlist789",
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncEnumerable(mockPlaylistVideoIds["playlist789"]));

        fixture.ApiFactory.MockYouTubeIntegration
            .SetupSequence(x =>
                x.GetVideosAsync(It.IsAny<IEnumerable<string>>(),
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockVideosFirstCall.ToList().AsReadOnly())
            .ReturnsAsync(mockVideosSecondCall.ToList().AsReadOnly());

        fixture.ApiFactory.MockCurrentChannelContext.Setup(x => x.GetRequiredChannelId())
            .Returns(testChannelId);

        // Act - First call
        var firstResponse = await fixture.HttpClient.PostAsync($"/api/channels/sync/current", null);
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

        // Act - Second call
        var secondResponse = await fixture.HttpClient.PostAsync($"/api/channels/sync/current", null);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);

        var secondResponseContent = await secondResponse.Content.ReadAsStringAsync();
        var secondSyncResult = JsonSerializer.Deserialize<ChannelSyncResult>(secondResponseContent,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        // Assert
        Assert.NotNull(secondSyncResult);
        Assert.Equal(0, secondSyncResult.VideosInserted);
        Assert.Equal(1, secondSyncResult.VideosUpdated);

        // Verify only one video exists and it was updated
        using var verificationScope = fixture.ApiServices.CreateScope();
        var verificationDatabaseContext = verificationScope.ServiceProvider.GetRequiredService<YouTubesterDb>();

        var videos = await verificationDatabaseContext.Videos
            .AsNoTracking()
            .Where(v => v.UploadsPlaylistId == testUploadsPlaylistId)
            .ToListAsync();

        Assert.Single(videos);
        Assert.Equal("video789", videos[0].VideoId);
        Assert.Equal("Updated Title", videos[0].Title);
    }

    private static async IAsyncEnumerable<T> CreateAsyncEnumerable<T>(IEnumerable<T> items)
    {
        foreach (var item in items)
        {
            yield return item;
        }

        await Task.CompletedTask;
    }
}