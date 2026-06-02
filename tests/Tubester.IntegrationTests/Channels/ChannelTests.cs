using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Tubester.Abstractions.Credits;
using Tubester.Abstractions.Users;
using Tubester.Application.Channels;
using Tubester.Domain;
using Tubester.Integration.Dtos;
using Tubester.IntegrationTests.TestHost;
using Tubester.Persistence;
using Tubester.Persistence.Credits;
using Xunit;

namespace Tubester.IntegrationTests.Channels;

[Collection(nameof(TestCollection))]
public class ChannelTests(TestFixture fixture)
{
    private readonly TestHelpers _helpers = new(fixture.ApiServices);

    [Fact]
    public async Task UpdatesDatabaseCorrectly()
    {
        // Arrange
        await fixture.CleanStateAsync();
        const string video1 = "video123";
        const string video2 = "video456";
        await _helpers.SeedTestDataAsync(new TestDataOptions
        {
            Videos = []
        });

        // Create mock video DTOs
        var mockVideos = new List<VideoDto>
        {
            new(
                video1,
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
                "etag-video123",
                null
            ),
            new(
                video2,
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
                "etag-video456",
                null
            )
        };

        var mockPlaylistData = new List<DetailedPlaylistDto>
        {
            new("playlist123", "Test Playlist 1", "Description 1", "public", "etag-playlist123"),
            new("playlist456", "Test Playlist 2", "Description 2", "private", "etag-playlist456")
        };

        var mockPlaylistVideoIds = new Dictionary<string, List<string>>
        {
            ["playlist123"] = [video1, video2],
            ["playlist456"] = [video2]
        };

        // Setup MockYouTubeIntegration
        fixture.ApiFactory.MockYouTubeIntegration
            .Setup(x => x.GetAllVideosAsync(TestConstants.UploadsPlaylistId,
                It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .Returns(TestHelpers.CreateAsyncEnumerable(mockVideos));

        fixture.ApiFactory.MockYouTubeIntegration
            .Setup(x => x.GetPlaylistsAsync(TestConstants.ChannelId,
                It.IsAny<CancellationToken>()))
            .Returns(TestHelpers.CreateAsyncEnumerable(mockPlaylistData));

        fixture.ApiFactory.MockYouTubeIntegration
            .Setup(x => x.GetPlaylistVideoIdsAsync("playlist123",
                It.IsAny<CancellationToken>()))
            .Returns(TestHelpers.CreateAsyncEnumerable(mockPlaylistVideoIds["playlist123"]));

        fixture.ApiFactory.MockYouTubeIntegration
            .Setup(x => x.GetPlaylistVideoIdsAsync("playlist456",
                It.IsAny<CancellationToken>()))
            .Returns(TestHelpers.CreateAsyncEnumerable(mockPlaylistVideoIds["playlist456"]));

        fixture.ApiFactory.MockYouTubeIntegration
            .Setup(x => x.GetVideosAsync(It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockVideos.ToList().AsReadOnly());

        // Act
        var response = await fixture.HttpClient.PostAsync($"/api/channels/sync/current", null);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var syncResult = await TestHelpers.DeserializeAsync<ChannelSyncResult>(response);

        Assert.NotNull(syncResult);
        Assert.Equal(2, syncResult.VideosInserted);
        Assert.Equal(0, syncResult.VideosUpdated);
        Assert.Equal(2, syncResult.PlaylistsInserted);
        Assert.Equal(0, syncResult.PlaylistsUpdated);
        Assert.Equal(3, syncResult.MembershipsAdded);

        // Assert database state
        using var verificationScope = fixture.ApiServices.CreateScope();
        var verificationDatabaseContext = verificationScope.ServiceProvider.GetRequiredService<TubesterDb>();

        // Verify videos were created
        var createdVideos = await verificationDatabaseContext.Videos
            .AsNoTracking()
            .Where(v => v.UploadsPlaylistId == TestConstants.UploadsPlaylistId)
            .OrderBy(v => v.VideoId)
            .ToListAsync();

        Assert.Equal(2, createdVideos.Count);

        var firstVideo = createdVideos.First(v => v.VideoId == video1);
        Assert.Equal(TestConstants.UploadsPlaylistId, firstVideo.UploadsPlaylistId);
        Assert.Equal(video1, firstVideo.VideoId);
        Assert.Equal("Test Video 1", firstVideo.Title);
        Assert.Equal("Test Description 1", firstVideo.Description);
        Assert.Equal(new List<string> { "tag1", "tag2" }, firstVideo.Tags);
        Assert.Equal(TimeSpan.FromMinutes(5), firstVideo.Duration);
        Assert.Equal(VideoVisibility.Public, firstVideo.Visibility);
        Assert.Equal(new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero), firstVideo.PublishedAt);
        Assert.Equal("22", firstVideo.CategoryId);
        Assert.Equal("en", firstVideo.DefaultLanguage);
        Assert.Equal("en", firstVideo.DefaultAudioLanguage);
        Assert.Equal("etag-video123", firstVideo.ETag);
        Assert.Null(firstVideo.CommentsAllowed);
        Assert.Equal(TestFixture.TestingDateTimeOffset, firstVideo.CachedAt);
        Assert.Equal(TestFixture.TestingDateTimeOffset, firstVideo.UpdatedAt);
        Assert.False(firstVideo.IsDirty);
        Assert.Equal(AiVideoOperationFlags.None, firstVideo.AiOperationsInProgress);
        Assert.False(firstVideo.IsAiTitleInProgress);
        Assert.False(firstVideo.IsAiDescriptionInProgress);
        Assert.False(firstVideo.IsAiTagsInProgress);
        Assert.False(firstVideo.IsAiPlaylistSuggestionInProgress);
        Assert.False(firstVideo.IsShort);
        Assert.Equal($"https://www.youtube.com/watch?v={video1}", firstVideo.Url);
        Assert.Equal($"https://i.ytimg.com/vi/{video1}/sddefault.jpg", firstVideo.ThumbnailUrl);

        var secondVideo = createdVideos.First(v => v.VideoId == video2);
        Assert.Equal(TestConstants.UploadsPlaylistId, secondVideo.UploadsPlaylistId);
        Assert.Equal(video2, secondVideo.VideoId);
        Assert.Equal("Test Video 2", secondVideo.Title);
        Assert.Equal("Test Description 2", secondVideo.Description);
        Assert.Equal(new List<string> { "tag3", "tag4" }, secondVideo.Tags);
        Assert.Equal(TimeSpan.FromMinutes(10), secondVideo.Duration);
        Assert.Equal(VideoVisibility.Unlisted, secondVideo.Visibility);
        Assert.Equal(new DateTimeOffset(2024, 1, 2, 12, 0, 0, TimeSpan.Zero), secondVideo.PublishedAt);
        Assert.Equal("23", secondVideo.CategoryId);
        Assert.Equal("en", secondVideo.DefaultLanguage);
        Assert.Equal("en", secondVideo.DefaultAudioLanguage);
        Assert.Equal("etag-video456", secondVideo.ETag);
        Assert.Null(secondVideo.CommentsAllowed);
        Assert.Equal(TestFixture.TestingDateTimeOffset, secondVideo.CachedAt);
        Assert.Equal(TestFixture.TestingDateTimeOffset, secondVideo.UpdatedAt);
        Assert.False(secondVideo.IsDirty);
        Assert.Equal(AiVideoOperationFlags.None, secondVideo.AiOperationsInProgress);
        Assert.False(secondVideo.IsAiTitleInProgress);
        Assert.False(secondVideo.IsAiDescriptionInProgress);
        Assert.False(secondVideo.IsAiTagsInProgress);
        Assert.False(secondVideo.IsAiPlaylistSuggestionInProgress);
        Assert.False(secondVideo.IsShort);
        Assert.Equal($"https://www.youtube.com/watch?v={video2}", secondVideo.Url);
        Assert.Equal($"https://i.ytimg.com/vi/{video2}/sddefault.jpg", secondVideo.ThumbnailUrl);

        // Verify playlists were created
        var createdPlaylists = await verificationDatabaseContext.Playlists
            .AsNoTracking()
            .Where(p => p.ChannelId == TestConstants.ChannelId)
            .OrderBy(p => p.PlaylistId)
            .ToListAsync();

        Assert.Equal(2, createdPlaylists.Count);
        Assert.Equal("playlist123", createdPlaylists[0].PlaylistId);
        Assert.Equal("Test Playlist 1", createdPlaylists[0].Title);
        Assert.Equal("Description 1", createdPlaylists[0].Description);
        Assert.Equal(PlaylistVisibility.Public, createdPlaylists[0].Visibility);
        Assert.Equal("playlist456", createdPlaylists[1].PlaylistId);
        Assert.Equal("Test Playlist 2", createdPlaylists[1].Title);
        Assert.Equal("Description 2", createdPlaylists[1].Description);
        Assert.Equal(PlaylistVisibility.Private, createdPlaylists[1].Visibility);

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
            .FirstOrDefaultAsync(c => c.ChannelId == TestConstants.ChannelId);

        Assert.NotNull(updatedChannel);
        Assert.NotNull(updatedChannel.LastUploadsCutoff);
        Assert.Equal(new DateTimeOffset(2024, 1, 2, 12, 0, 0, TimeSpan.Zero), updatedChannel.LastUploadsCutoff);
    }

    [Fact]
    public async Task CalledTwice_IsIdempotentAndUpdatesExistingData()
    {
        // Arrange
        await fixture.CleanStateAsync();
        await _helpers.SeedTestDataAsync();

        // Create mock video DTOs
        var mockVideosFirstCall = new List<VideoDto>
        {
            new(
                TestConstants.TargetVideoId,
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
                "etag-video789-v1",
                null
            )
        };

        var mockVideosSecondCall = new List<VideoDto>
        {
            new(
                TestConstants.TargetVideoId,
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
                "etag-video789-v2",
                null
            )
        };

        var mockPlaylistData = new List<DetailedPlaylistDto> { new("playlist789", "Test Playlist", "Description", "public", "etag-video789-v1") };

        var mockPlaylistVideoIds = new Dictionary<string, List<string>> { ["playlist789"] = ["video789"] };

        // Setup MockYouTubeIntegration for first call
        fixture.ApiFactory.MockYouTubeIntegration
            .SetupSequence(x =>
                x.GetAllVideosAsync(TestConstants.UploadsPlaylistId,
                    It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .Returns(TestHelpers.CreateAsyncEnumerable(mockVideosFirstCall))
            .Returns(TestHelpers.CreateAsyncEnumerable(mockVideosSecondCall));

        fixture.ApiFactory.MockYouTubeIntegration
            .Setup(x => x.GetPlaylistsAsync(TestConstants.ChannelId,
                It.IsAny<CancellationToken>()))
            .Returns(TestHelpers.CreateAsyncEnumerable(mockPlaylistData));

        fixture.ApiFactory.MockYouTubeIntegration
            .Setup(x => x.GetPlaylistVideoIdsAsync("playlist789",
                It.IsAny<CancellationToken>()))
            .Returns(TestHelpers.CreateAsyncEnumerable(mockPlaylistVideoIds["playlist789"]));

        fixture.ApiFactory.MockYouTubeIntegration
            .SetupSequence(x =>
                x.GetVideosAsync(It.IsAny<IEnumerable<string>>(),
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockVideosFirstCall.ToList().AsReadOnly())
            .ReturnsAsync(mockVideosSecondCall.ToList().AsReadOnly());

        // Act - First call
        var firstResponse = await fixture.HttpClient.PostAsync($"/api/channels/sync/current", null);
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

        // Act - Second call
        var secondResponse = await fixture.HttpClient.PostAsync($"/api/channels/sync/current", null);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);

        var secondSyncResult = await TestHelpers.DeserializeAsync<ChannelSyncResult>(secondResponse);

        // Assert
        Assert.NotNull(secondSyncResult);
        Assert.Equal(0, secondSyncResult.VideosInserted);
        Assert.Equal(1, secondSyncResult.VideosUpdated);

        // Verify only one video exists and it was updated
        using var verificationScope = fixture.ApiServices.CreateScope();
        var verificationDatabaseContext = verificationScope.ServiceProvider.GetRequiredService<TubesterDb>();

        var videos = await verificationDatabaseContext.Videos
            .AsNoTracking()
            .Where(v => v.UploadsPlaylistId == TestConstants.UploadsPlaylistId)
            .ToListAsync();

        Assert.Single(videos);
        Assert.Equal(TestConstants.TargetVideoId, videos[0].VideoId);
        Assert.Equal("Updated Title", videos[0].Title);
    }

    [Fact]
    public async Task WithoutSubscription_AssignsFreeSubscriptionAndGrantsCredits()
    {
        // Arrange
        await fixture.CleanStateAsync();
        await _helpers.SeedTestDataAsync(
            new TestDataOptions
            {
                CreateSubscription = false
            });

        SetupMinimalSyncMocks();

        // Act
        var response = await fixture.HttpClient.PostAsync("/api/channels/sync/current", null);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var verificationScope = fixture.ApiServices.CreateScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<TubesterDb>();

        // Verify subscription was created with free plan
        var subscription = await verificationDb.Subscriptions
            .AsNoTracking()
            .Include(entity => entity.Plan)
            .SingleOrDefaultAsync(entity => entity.UserId == TestConstants.UserId);

        Assert.NotNull(subscription);
        Assert.Equal("free", subscription.Plan.Code);
        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
        Assert.Equal(TestFixture.TestingDateTimeOffset, subscription.PeriodStartUtc);
        Assert.Equal(TestFixture.TestingDateTimeOffset.AddDays(30), subscription.PeriodEndUtc);

        // Verify wallet was created with monthly credits
        var wallet = await verificationDb.Wallets
            .AsNoTracking()
            .SingleOrDefaultAsync(entity => entity.UserId == TestConstants.UserId);

        Assert.NotNull(wallet);
        Assert.Equal(TestConstants.MonthlyCredits, wallet.Balance);
        Assert.Equal(TestFixture.TestingDateTimeOffset, wallet.PeriodStartUtc);
        Assert.Equal(TestFixture.TestingDateTimeOffset.AddDays(30), wallet.PeriodEndUtc);

        // Verify ledger has a single PeriodGrant entry
        var ledgerEntries = await verificationDb.LedgerEntries
            .AsNoTracking()
            .Where(entry => entry.UserId == TestConstants.UserId)
            .ToListAsync();

        var grantEntry = Assert.Single(ledgerEntries);
        Assert.Equal("PeriodGrant", grantEntry.ActionType);
        Assert.Equal(TestConstants.MonthlyCredits, grantEntry.Delta);
    }

    [Fact]
    public async Task WithActiveSubscription_SyncsWithoutModifyingCredits()
    {
        // Arrange
        await fixture.CleanStateAsync();
        var testData = await _helpers.SeedTestDataAsync();
        var serviceScope = fixture.ApiServices.CreateScope();
        var creditStore = serviceScope.ServiceProvider.GetRequiredService<ICreditsStore>();
        var idempotencyKey = $"grant:{TestConstants.UserId}:{TestFixture.TestingDateTimeOffset.ToUniversalTime():O}";
        await creditStore.GrantPeriodCreditsAsync(TestConstants.UserId, testData.Subscription!.PeriodStartUtc,
            testData.Subscription.PeriodEndUtc, TestConstants.MonthlyCredits, idempotencyKey, TestFixture.TestingDateTimeOffset, CancellationToken.None);
        SetupMinimalSyncMocks();
        // Act
        var response = await fixture.HttpClient.PostAsync("/api/channels/sync/current", null);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var syncResult = await TestHelpers.DeserializeAsync<ChannelSyncResult>(response);
        Assert.NotNull(syncResult);

        using var verificationScope = fixture.ApiServices.CreateScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<TubesterDb>();

        // Verify subscription is unchanged
        var subscription = await verificationDb.Subscriptions
            .AsNoTracking()
            .SingleOrDefaultAsync(entity => entity.UserId == TestConstants.UserId);

        Assert.NotNull(subscription);
        Assert.Equal(SubscriptionStatus.Active, subscription.Status);

        // Verify wallet balance is unchanged
        var wallet = await verificationDb.Wallets
            .AsNoTracking()
            .SingleOrDefaultAsync(entity => entity.UserId == TestConstants.UserId);

        Assert.NotNull(wallet);
        Assert.Equal(TestConstants.MonthlyCredits, wallet.Balance);

        // Verify no new ledger entries were created
        var ledgerEntries = await verificationDb.LedgerEntries
            .AsNoTracking()
            .Where(entry => entry.UserId == TestConstants.UserId)
            .ToListAsync();

        var grantEntry = Assert.Single(ledgerEntries);
        Assert.Equal("PeriodGrant", grantEntry.ActionType);
        Assert.Equal(TestConstants.MonthlyCredits, grantEntry.Delta);
    }

    [Fact]
    public async Task WithInactiveSubscription_ReturnsForbiddenAndDoesNotModifyCredits()
    {
        // Arrange
        await fixture.CleanStateAsync();

        const string testChannelId = "UCSubInactiveChannel";
        const string testUploadsPlaylistId = "PLSubInactiveUploads";
        const string userId = MockAuthenticationExtensions.TestSub;

        using (var scope = fixture.ApiServices.CreateScope())
        {
            var databaseContext = scope.ServiceProvider.GetRequiredService<TubesterDb>();

            databaseContext.Users.Add(User.Create(
                userId,
                MockAuthenticationExtensions.TestEmail,
                MockAuthenticationExtensions.TestName,
                MockAuthenticationExtensions.TestPicture,
                TestFixture.TestingDateTimeOffset));

            databaseContext.Channels.Add(Channel.Create(
                testChannelId,
                userId,
                "SubInactiveChannel",
                testUploadsPlaylistId,
                TestFixture.TestingDateTimeOffset));

            var plan = new Plan
            {
                Code = "InactiveTestPlan",
                Name = "Inactive Test Plan",
                MonthlyCredits = 100,
                IsActive = true,
                CreatedAtUtc = TestFixture.TestingDateTimeOffset,
                UpdatedAtUtc = TestFixture.TestingDateTimeOffset
            };

            databaseContext.Plans.Add(plan);
            await databaseContext.SaveChangesAsync();

            databaseContext.Subscriptions.Add(new Subscription
            {
                UserId = userId,
                PlanId = plan.Id,
                PeriodStartUtc = TestFixture.TestingDateTimeOffset,
                PeriodEndUtc = TestFixture.TestingDateTimeOffset.AddMonths(1),
                Status = SubscriptionStatus.Unknown
            });

            await databaseContext.SaveChangesAsync();
        }

        // No YouTube mocks needed — sync returns before reaching YouTube

        // Act
        var response = await fixture.HttpClient.PostAsync("/api/channels/sync/current", null);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        using var verificationScope = fixture.ApiServices.CreateScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<TubesterDb>();

        // Verify subscription is still inactive
        var subscription = await verificationDb.Subscriptions
            .AsNoTracking()
            .SingleOrDefaultAsync(entity => entity.UserId == userId);

        Assert.NotNull(subscription);
        Assert.Equal(SubscriptionStatus.Unknown, subscription.Status);

        // Verify no wallet was created
        var wallet = await verificationDb.Wallets
            .AsNoTracking()
            .SingleOrDefaultAsync(entity => entity.UserId == userId);

        Assert.Null(wallet);

        // Verify no ledger entries were created
        var ledgerEntries = await verificationDb.LedgerEntries
            .AsNoTracking()
            .Where(entry => entry.UserId == userId)
            .ToListAsync();

        Assert.Empty(ledgerEntries);
    }

    [Fact]
    public async Task Current_DoesNotOverrideCurrentVideoTitleDescriptionTagsAndPlaylist()
    {
        // Arrange
        await fixture.CleanStateAsync();

        var video = TestHelpers.GetTargetVideo();
        video.MarkAsDirty(TestFixture.TestingDateTimeOffset);
        var currentPlaylist = TestHelpers.GetPlaylist("current-playlist");

        var testData = await _helpers.SeedTestDataAsync(new TestDataOptions
        {
            Videos = [video],
            Playlists = [currentPlaylist],
            VideoPlaylists =
            [
                VideoPlaylist.Create(TestConstants.TargetVideoId, currentPlaylist.PlaylistId)
            ]
        });

        var remoteVideo = new VideoDto(
            testData.Video!.VideoId,
            "Remote Title",
            "Remote Description",
            ["remote-tag-1", "remote-tag-2"],
            TimeSpan.FromMinutes(3),
            "public",
            false,
            TestFixture.TestingDateTimeOffset.AddDays(-2),
            TestConstants.TargetVideoCategoryId,
            TestConstants.DefaultLanguage,
            TestConstants.DefaultAudioLanguage,
            "remote-etag",
            null
        );
        var remoteVideos = new List<VideoDto>
        {
            remoteVideo
        };

        var remotePlaylist = new DetailedPlaylistDto("remote-playlist-id",
            "remote-playlist-title",
            "remote-playlist-description",
            "public",
            "remote-playlist-etag");
        var remotePlaylists = new List<DetailedPlaylistDto>
        {
            remotePlaylist
        };

        fixture.ApiFactory.MockYouTubeIntegration
            .Setup(x => x.GetAllVideosAsync(
                TestConstants.UploadsPlaylistId,
                It.IsAny<DateTimeOffset?>(),
                It.IsAny<CancellationToken>()))
            .Returns(TestHelpers.CreateAsyncEnumerable(remoteVideos));

        fixture.ApiFactory.MockYouTubeIntegration
            .Setup(x => x.GetPlaylistsAsync(
                TestConstants.ChannelId,
                It.IsAny<CancellationToken>()))
            .Returns(TestHelpers.CreateAsyncEnumerable(remotePlaylists));

        fixture.ApiFactory.MockYouTubeIntegration
            .Setup(x => x.GetPlaylistVideoIdsAsync(
                currentPlaylist.PlaylistId,
                It.IsAny<CancellationToken>()))
            .Returns(TestHelpers.CreateAsyncEnumerable(Array.Empty<string>()));

        fixture.ApiFactory.MockYouTubeIntegration
            .Setup(x => x.GetPlaylistVideoIdsAsync(
                remotePlaylist.Id,
                It.IsAny<CancellationToken>()))
            .Returns(TestHelpers.CreateAsyncEnumerable(remoteVideos.Select(v => v.VideoId)));

        // Act
        var response = await fixture.HttpClient.PostAsync("/api/channels/sync/current", null);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await _helpers.AssertVideoAsync(testData.Video!);
        await _helpers.AssertVideoPlaylistsAsync(testData.Video!.VideoId, currentPlaylist.PlaylistId);
    }

    [Fact]
    public async Task Current_WithDirtyVideoFilteredOutByUploadsCutoff_DoesNotModifyDetailsOrPlaylists()
    {
        // Arrange
        await fixture.CleanStateAsync();

        var cutoff = TestFixture.TestingDateTimeOffset;
        var video = TestHelpers.GetTargetVideo();
        video.MarkAsDirty(TestFixture.TestingDateTimeOffset);
        var currentPlaylist = TestHelpers.GetPlaylist("current-playlist");

        var testData = await _helpers.SeedTestDataAsync(new TestDataOptions
        {
            Videos = [video],
            Playlists = [currentPlaylist],
            VideoPlaylists =
            [
                VideoPlaylist.Create(TestConstants.TargetVideoId, currentPlaylist.PlaylistId)
            ]
        });

        using (var setupScope = fixture.ApiServices.CreateScope())
        {
            var databaseContext = setupScope.ServiceProvider.GetRequiredService<TubesterDb>();
            var channel = await databaseContext.Channels
                .SingleAsync(c => c.ChannelId == TestConstants.ChannelId);

            channel.AdvanceUploadsCutoff(cutoff, TestFixture.TestingDateTimeOffset);
            await databaseContext.SaveChangesAsync();
        }

        var remotePlaylist = new DetailedPlaylistDto(
            "remote-playlist-id",
            "remote-playlist-title",
            "remote-playlist-description",
            "public",
            "remote-playlist-etag");

        fixture.ApiFactory.MockYouTubeIntegration
            .Setup(x => x.GetAllVideosAsync(
                TestConstants.UploadsPlaylistId,
                cutoff,
                It.IsAny<CancellationToken>()))
            .Returns(TestHelpers.CreateAsyncEnumerable(Array.Empty<VideoDto>()));

        fixture.ApiFactory.MockYouTubeIntegration
            .Setup(x => x.GetPlaylistsAsync(
                TestConstants.ChannelId,
                It.IsAny<CancellationToken>()))
            .Returns(TestHelpers.CreateAsyncEnumerable([remotePlaylist]));

        fixture.ApiFactory.MockYouTubeIntegration
            .Setup(x => x.GetPlaylistVideoIdsAsync(
                remotePlaylist.Id,
                It.IsAny<CancellationToken>()))
            .Returns(TestHelpers.CreateAsyncEnumerable([TestConstants.TargetVideoId]));

        // Act
        var response = await fixture.HttpClient.PostAsync("/api/channels/sync/current", null);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var syncResult = await TestHelpers.DeserializeAsync<ChannelSyncResult>(response);
        Assert.NotNull(syncResult);
        Assert.Equal(0, syncResult.VideosInserted);
        Assert.Equal(0, syncResult.VideosUpdated);
        Assert.Equal(0, syncResult.PlaylistsInserted);
        Assert.Equal(0, syncResult.PlaylistsUpdated);
        Assert.Equal(0, syncResult.MembershipsAdded);
        Assert.Equal(0, syncResult.MembershipsRemoved);

        await _helpers.AssertVideoAsync(testData.Video!);
        await _helpers.AssertVideoPlaylistsAsync(testData.Video!.VideoId, currentPlaylist.PlaylistId);

        using var verificationScope = fixture.ApiServices.CreateScope();
        var verificationDatabaseContext = verificationScope.ServiceProvider.GetRequiredService<TubesterDb>();

        var playlists = await verificationDatabaseContext.Playlists
            .AsNoTracking()
            .Where(p => p.ChannelId == TestConstants.ChannelId)
            .ToListAsync();

        var playlist = Assert.Single(playlists);
        Assert.Equal(currentPlaylist.PlaylistId, playlist.PlaylistId);
        Assert.Equal(currentPlaylist.Title, playlist.Title);
        Assert.Equal(currentPlaylist.Description, playlist.Description);
        Assert.Equal(currentPlaylist.Visibility, playlist.Visibility);
    }

    private void SetupMinimalSyncMocks(string channelId = TestConstants.ChannelId, string uploadsPlaylistId = TestConstants.UploadsPlaylistId)
    {
        fixture.ApiFactory.MockYouTubeIntegration
            .Setup(x => x.GetAllVideosAsync(uploadsPlaylistId,
                It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .Returns(TestHelpers.CreateAsyncEnumerable(Array.Empty<VideoDto>()));

        fixture.ApiFactory.MockYouTubeIntegration
            .Setup(x => x.GetPlaylistsAsync(channelId,
                It.IsAny<CancellationToken>()))
            .Returns(TestHelpers.CreateAsyncEnumerable(Array.Empty<DetailedPlaylistDto>()));
    }
}