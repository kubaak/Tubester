using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tubester.Application.Account;
using Tubester.Application.Contracts.Videos;
using Tubester.Domain;
using Tubester.IntegrationTests.TestHost;
using Tubester.Persistence;
using Tubester.Persistence.Credits;
using Xunit;

namespace Tubester.IntegrationTests.Users;

[Collection(nameof(TestCollection))]
public sealed class UserDataDeletionTests(TestFixture fixture)
{
    private readonly TestHelpers _helpers = new(fixture.ApiServices);

    [Fact]
    public async Task DeleteUser_ClearsUserData()
    {

        // Arrange
        await fixture.CleanStateAsync();
        var video = TestHelpers.GetTargetVideo();
        var reply = TestHelpers.GetReply(video.VideoId);
        var playlist = TestHelpers.GetPlaylist("uploads");
        var videoPlaylist = VideoPlaylist.Create(video.VideoId, playlist.PlaylistId);
        var testData = await _helpers.SeedTestDataAsync(new TestDataOptions
        {
            Videos = [video],
            Replies = [reply],
            Playlists = [playlist],
            VideoPlaylists = [videoPlaylist]
        });
        var accountSettingsService = fixture.ApiServices.GetRequiredService<IAccountSettingsService>();
        await accountSettingsService.GetOrCreateAsync(testData.User.Id, CancellationToken.None);
        //Some dummy Action to top up wallet, create logs, etc.
        var request = new AiVideoTemplateRequest
        {
            TargetVideoId = testData.Video!.VideoId,
            PromptEnrichment = "XXX",
            ExpectedCreditCost = TestConstants.AiTitleEnqueuedCost + TestConstants.AiDescriptionEnqueuedCost + TestConstants.AiTagsEnqueuedCost
        };
        var requestMessage = new HttpRequestMessage(HttpMethod.Post, "/api/videos/ai-template")
        {
            Content = TestHelpers.CreateJsonContent(request)
        };
        requestMessage.Headers.Add("OperationId", TestHelpers.NewOperationId());
        await fixture.HttpClient.SendAsync(requestMessage);

        // Act - delete user
        var response = await fixture.HttpClient.DeleteAsync("api/users/me");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // Assert
        using var scope = fixture.ApiServices.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TubesterDb>();

        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.Id == testData.User.Id);

        Assert.NotNull(user);
        Assert.True(user.IsDeleted);
        Assert.NotNull(user.DeletedAt);
        Assert.Null(user.Email);
        Assert.Null(user.Name);
        Assert.Null(user.Picture);

        // Channel data should be deleted
        var channels = await dbContext.Channels
            .Where(c => c.UserId == testData.User.Id)
            .ToListAsync();

        Assert.Empty(channels);

        // Channel settings should be deleted
        var settings = await dbContext.ChannelSettings
            .Where(s => s.ChannelId == testData.Channel.ChannelId)
            .ToListAsync();

        Assert.Empty(settings);

        // Videos imported from YouTube should be deleted
        var videos = await dbContext.Videos
            .Where(v => v.UploadsPlaylistId == testData.Channel.UploadsPlaylistId)
            .ToListAsync();

        Assert.Empty(videos);

        // Replies generated/imported through videos should be deleted
        var replies = await dbContext.Replies
            .Where(r => r.VideoId == testData.Video!.VideoId)
            .ToListAsync();

        Assert.Empty(replies);

        // VideoPlaylist links should be deleted
        var videoPlaylists = await dbContext.VideoPlaylists
            .Where(vp => vp.VideoId == testData.Video!.VideoId)
            .ToListAsync();

        Assert.Empty(videoPlaylists);

        // The uploads playlist should be deleted if playlists are user/channel-owned YouTube data
        var playlists = await dbContext.Playlists
            .Where(p => p.PlaylistId == playlist.PlaylistId)
            .ToListAsync();

        Assert.Empty(playlists);

        // Account settings should be deleted/reset so a future login starts fresh
        var accountSettings = await dbContext.AccountSettings
            .Where(s => s.UserId == testData.User.Id)
            .ToListAsync();

        Assert.Empty(accountSettings);

        // Subscription should be canceled
        var subscription = await dbContext.Subscriptions
            .FirstOrDefaultAsync(s => s.UserId == testData.User.Id);

        Assert.NotNull(subscription);
        Assert.Equal(SubscriptionStatus.Cancelled, subscription.Status);
        Assert.True(subscription.PeriodEndUtc <= TestFixture.TestingDateTimeOffset);

        // Wallet should be reset/closed
        var wallet = await dbContext.Wallets
            .FirstOrDefaultAsync(w => w.UserId == testData.User.Id);

        Assert.NotNull(wallet);
        Assert.Equal(0, wallet.Balance);
        Assert.True(wallet.PeriodEndUtc <= TestFixture.TestingDateTimeOffset);

        // Ledger entries may be retained, but only as accounting records
        var ledgerEntries = await dbContext.LedgerEntries
            .Where(l => l.UserId == testData.User.Id)
            .ToListAsync();

        Assert.NotEmpty(ledgerEntries);

        // Make sure retained ledger entries do not contain YouTube/user-derived data.
        // Adjust property names if your LedgerEntry uses different text fields.
        Assert.All(ledgerEntries, entry =>
        {
            var serializedEntry = entry.ToString();

            Assert.DoesNotContain(testData.User.Email!, serializedEntry, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(testData.User.Name!, serializedEntry, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(testData.Channel.ChannelId, serializedEntry, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(testData.Channel.Name, serializedEntry, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(testData.Video!.VideoId, serializedEntry, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(testData.Video.Title!, serializedEntry, StringComparison.OrdinalIgnoreCase);
        });

        // Subscription history may be retained, but must not contain Google/YouTube-derived data
        var subscriptionHistory = await dbContext.SubscriptionHistories
            .Where(h => h.UserId == testData.User.Id)
            .ToListAsync();

        Assert.NotEmpty(subscriptionHistory);

        Assert.All(subscriptionHistory, history =>
        {
            var serializedHistory = history.ToString();

            Assert.DoesNotContain(testData.User.Email!, serializedHistory, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(testData.User.Name!, serializedHistory, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(testData.Channel.ChannelId, serializedHistory, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(testData.Channel.Name, serializedHistory, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(testData.Video!.VideoId, serializedHistory, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(testData.Video.Title!, serializedHistory, StringComparison.OrdinalIgnoreCase);
        });

        // User events should be minimized/anonymized.
        // At least the deletion audit event should exist.
        var events = await dbContext.UserEvents
            .Where(e => e.UserId == testData.User.Id)
            .ToListAsync();

        Assert.NotEmpty(events);
        Assert.Contains(events, e => e.EventType == "UserDataDeletionRequested");

        // Retained user events must not contain YouTube-derived or personal data.
        // Adjust/remove Payload assertion depending on your UserEvent properties.
        Assert.All(events, userEvent =>
        {
            var serializedEvent = userEvent.ToString();

            Assert.DoesNotContain(testData.User.Email!, serializedEvent, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(testData.User.Name!, serializedEvent, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(testData.Channel.ChannelId, serializedEvent, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(testData.Channel.Name, serializedEvent, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(testData.Video!.VideoId, serializedEvent, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(testData.Video.Title!, serializedEvent, StringComparison.OrdinalIgnoreCase);
        });
    }


    [Fact]
    public async Task DeleteUser_IsIdempotent()
    {
        await fixture.CleanStateAsync();
        _ = await _helpers.SeedTestDataAsync();
        // Arrange - first deletion
        await fixture.HttpClient.DeleteAsync("api/users/me");

        // Act - second deletion (should not fail)
        var response = await fixture.HttpClient.DeleteAsync("api/users/me");

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
}