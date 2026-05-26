using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tubester.Abstractions;
using Tubester.Abstractions.ApplicationConfiguration;
using Tubester.Abstractions.Credits;
using Tubester.Abstractions.Users;
using Tubester.Abstractions.Videos;
using Tubester.Application.Contracts.Videos;
using Tubester.Domain;
using Tubester.Integration;
using Tubester.Persistence;
using Tubester.Persistence.Credits;
using Xunit;
using StringContent = System.Net.Http.StringContent;

namespace Tubester.IntegrationTests.TestHost;

public sealed class TestHelpers(IServiceProvider serviceProvider)
{
    public static StringContent CreateJsonContent<T>(T request)
    {
        var json = JsonSerializer.Serialize(request, TubesterJsonSerializerOptions.DefaultWrite);
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    public static async Task<T> DeserializeAsync<T>(HttpResponseMessage response)
    {
        var responseContent = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<T>(responseContent, TubesterJsonSerializerOptions.DefaultWrite);

        Assert.NotNull(result);

        return result;
    }

    public static string NewOperationId() => $"operation-{Guid.NewGuid():N}";

    public async Task<VideoTestDataResult> SeedTestDataAsync(TestDataOptions? videoTestDataOptions = null)
    {
        var options = videoTestDataOptions ?? new TestDataOptions();

        if (options is { CreateSubscription: true, Plans.Count: > 0 })
        {
            throw new InvalidOperationException("Cannot create default plan and subscription with custom plans provided.");
        }

        using var serviceScope = serviceProvider.CreateScope();
        var databaseContext = serviceScope.ServiceProvider.GetRequiredService<TubesterDb>();

        var user = User.Create(
            options.UserId,
            MockAuthenticationExtensions.TestEmail,
            MockAuthenticationExtensions.TestName,
            MockAuthenticationExtensions.TestPicture,
            TestFixture.TestingDateTimeOffset);

        var channel = Channel.Create(
            options.ChannelId,
            options.UserId,
            options.ChannelName,
            options.UploadsPlaylistId,
            TestFixture.TestingDateTimeOffset);

        await databaseContext.Users.AddAsync(user, CancellationToken.None);
        await databaseContext.Channels.AddAsync(channel, CancellationToken.None);
        await databaseContext.SaveChangesAsync(CancellationToken.None);

        var channelSettings = ChannelSettings.CreateDefault(TestConstants.ChannelId, TestFixture.TestingDateTimeOffset);
        channelSettings.Apply(options.EnableCommentScan, true, 10,
            10, "English", "responseForNonTextualComments", TestFixture.TestingDateTimeOffset);
        databaseContext.ChannelSettings.Add(channelSettings);

        if (options.ApplicationConfigurations.Count > 0)
        {
            databaseContext.ApplicationConfigurations.AddRange(options.ApplicationConfigurations);
        }

        if (options.Videos.Count > 0)
        {
            databaseContext.Videos.AddRange(options.Videos);
        }

        await databaseContext.SaveChangesAsync(CancellationToken.None);

        if (options.Playlists.Count > 0)
        {
            databaseContext.Playlists.AddRange(options.Playlists);
        }

        if (options.Replies.Count > 0)
        {
            databaseContext.Replies.AddRange(options.Replies);
        }

        await databaseContext.SaveChangesAsync(CancellationToken.None);

        if (options.ActionCosts.Count > 0)
        {
            databaseContext.ActionCosts.AddRange(options.ActionCosts);
        }

        await databaseContext.SaveChangesAsync(CancellationToken.None);

        var plan = new Plan
        {
            Code = TestConstants.FreePlanCode,
            Name = TestConstants.FreePlanName,
            MonthlyCredits = options.MonthlyCredits,
            IsActive = true,
            CreatedAtUtc = TestFixture.TestingDateTimeOffset,
            UpdatedAtUtc = TestFixture.TestingDateTimeOffset
        };

        await databaseContext.Plans.AddAsync(plan, CancellationToken.None);
        await databaseContext.SaveChangesAsync(CancellationToken.None);
        Subscription? subscription = null;
        if (options.CreateSubscription)
        {
            subscription = new Subscription
            {
                UserId = TestConstants.UserId,
                PlanId = plan.Id,
                PeriodStartUtc = TestFixture.TestingDateTimeOffset,
                PeriodEndUtc = TestFixture.TestingDateTimeOffset.AddMonths(1),
                Status = SubscriptionStatus.Active
            };
            await databaseContext.Subscriptions.AddAsync(subscription, CancellationToken.None);
        }

        if (options.Plans.Count > 0)
        {
            await databaseContext.Plans.AddRangeAsync(options.Plans, CancellationToken.None);
        }

        await databaseContext.SaveChangesAsync(CancellationToken.None);

        if (options.VideoPlaylists.Count > 0)
        {
            databaseContext.VideoPlaylists.AddRange(options.VideoPlaylists);
        }

        await databaseContext.SaveChangesAsync(CancellationToken.None);

        return new VideoTestDataResult(user, channel, plan, subscription, options.Videos.FirstOrDefault(), channelSettings);
    }

    public static void SetVideoProperties(Video video, string expectedTitle, string expectedDescription, string[] expectedTags)
    {
        SetProperty(video, nameof(video.UpdatedAt), TestFixture.TestingDateTimeOffset);
        SetProperty(video, nameof(video.Title), expectedTitle);
        SetProperty(video, nameof(video.Description), expectedDescription);
        SetProperty(video, nameof(video.Tags), expectedTags);
        SetProperty<string>(video, nameof(video.ETag), null); //Etag will be reset
    }

    public async Task AssertVideoAsync(Video video)
    {
        using var verifyScope = serviceProvider.CreateScope();
        var dbContext = verifyScope.ServiceProvider.GetRequiredService<TubesterDb>();
        var existing = await dbContext.Videos
            .Where(e => e.VideoId == video.VideoId)
            .SingleOrDefaultAsync();

        Assert.NotNull(existing);
        Assert.Equivalent(video, existing, true);
    }

    public async Task AssertUserEventAsync(CreditActionType actionType, string userId, string videoId)
    {
        using var verifyScope = serviceProvider.CreateScope();
        var dbContext = verifyScope.ServiceProvider.GetRequiredService<TubesterDb>();

        var events = await dbContext.UserEvents
            .Where(e => e.EventType == actionType.ToString())
            .ToListAsync();

        Assert.Single(events);

        var userEvent = events[0];

        Assert.Equal(userId, userEvent.UserId);
        Assert.Equal(videoId, userEvent.VideoId);
        Assert.Null(userEvent.CommentId);
        Assert.False(string.IsNullOrWhiteSpace(userEvent.MetadataJson));
    }

    public async Task AssertVideoPlaylistsAsync(string videoId, params string[] expectedPlaylistIds)
    {
        using var verifyScope = serviceProvider.CreateScope();
        var databaseContext = verifyScope.ServiceProvider.GetRequiredService<TubesterDb>();

        var memberships = await databaseContext.VideoPlaylists
            .Where(vp => vp.VideoId == videoId)
            .ToListAsync();

        Assert.Equal(expectedPlaylistIds.Length, memberships.Count);

        foreach (var playlistId in expectedPlaylistIds)
        {
            Assert.Contains(memberships, vp => vp.PlaylistId == playlistId);
        }
    }

    public async Task AssertReplyAsync(Reply reply)
    {
        using var verifyScope = serviceProvider.CreateScope();
        var dbContext = verifyScope.ServiceProvider.GetRequiredService<TubesterDb>();
        var existing = await dbContext.Replies
            .Where(e => e.CommentId == reply.CommentId)
            .SingleOrDefaultAsync();

        Assert.NotNull(existing);
        Assert.Equivalent(reply, existing, true);
    }

    public async Task AssertProblemDetailsAsync(
        HttpResponseMessage response,
        int expectedStatusCode,
        string expectedTitle,
        string expectedDetail)
    {
        var problemDetails = await DeserializeAsync<ProblemDetails>(response);

        Assert.NotNull(problemDetails);
        Assert.Equal(expectedStatusCode, problemDetails.Status);
        Assert.Equal(expectedTitle, problemDetails.Title);
        Assert.Equal(expectedDetail, problemDetails.Detail);
        Assert.True(problemDetails.Extensions.ContainsKey("traceId"));
    }

    public static Video GetTargetVideo(string videoId = TestConstants.TargetVideoId, VideoVisibility visibility = VideoVisibility.Public
        , bool isCommentable = true)
    {
        var video = Video.Create(
            TestConstants.UploadsPlaylistId,
            videoId,
            $"Target Video Title {videoId}",
            $"Target Video Description {videoId}",
            TestFixture.TestingDateTimeOffset.AddDays(-2),
            TimeSpan.FromMinutes(3),
            visibility,
            ["target"],
            TestConstants.TargetVideoCategoryId,
            TestConstants.DefaultLanguage,
            TestConstants.DefaultAudioLanguage,
            TestFixture.TestingDateTimeOffset.AddDays(-1),
            "etag-target",
            isCommentable
        );
        return video;
    }

    public static Video GetSourceVideo()
    {
        return Video.Create(
            TestConstants.UploadsPlaylistId,
            "sourceVideoId",
            "Source Video Title",
            "Source Video Description",
            TestFixture.TestingDateTimeOffset.AddDays(-1),
            TimeSpan.FromMinutes(5),
            VideoVisibility.Public,
            ["source", "template"],
            "22",
            "en",
            "en",
            TestFixture.TestingDateTimeOffset,
            "etag-source",
            true
        );
    }

    public static Playlist GetPlaylist(string playlistId = TestConstants.PlaylistId, PlaylistVisibility visibility = PlaylistVisibility.Public)
    {
        var playlist = Playlist.Create(
            playlistId,
            TestConstants.ChannelId,
            $"My Playlist {playlistId}",
            $"My Playlist Description{playlistId}",
            visibility,
            TestFixture.TestingDateTimeOffset);
        return playlist;
    }

    public static Reply GetReply(string commentId, string videoId = TestConstants.TargetVideoId, bool isSuggested = false)
    {
        var reply = Reply.Create(
            commentId,
            videoId,
            $"Title {commentId} {videoId} ",
            $"Comment text {commentId} {videoId} ",
            TestFixture.TestingDateTimeOffset,
            TestFixture.TestingDateTimeOffset.AddDays(-1));

        if (isSuggested)
        {
            reply.SuggestText($"Suggested text {commentId} {videoId} ", TestFixture.TestingDateTimeOffset.AddMinutes(5));
        }

        return reply;
    }

    public static void AssertVideoListItemDto(VideoListItemDto listItemDto, Video video)
    {
        Assert.Equal(video.VideoId, listItemDto.VideoId);
        Assert.Equal(video.Title, listItemDto.Title);
        Assert.Equal(video.PublishedAt, listItemDto.PublishedAt);
    }

    public static void AssertVideoDetails(
        VideoDetailsDto? videoDetails,
        Video video,
        PlaylistDto[]? playlists = null)
    {
        Assert.NotNull(videoDetails);

        Assert.Equal(video.Title, videoDetails.Title);
        Assert.Equal(video.Description, videoDetails.Description);
        Assert.NotNull(videoDetails.Tags);
        Assert.Equal(video.Tags, videoDetails.Tags);
        Assert.False(videoDetails.IsAiTitleInProgress);

        Assert.NotNull(videoDetails.Category);
        Assert.Equal(video.CategoryId, videoDetails.Category.Id);
        Assert.Null(videoDetails.Category.Name);
        Assert.Equal(video.DefaultLanguage, videoDetails.DefaultLanguage);
        Assert.Equal(video.DefaultAudioLanguage, videoDetails.DefaultAudioLanguage);

        var expectedPlaylists = playlists ?? [];

        Assert.Equal(expectedPlaylists.Length, videoDetails.Playlists.Length);

        foreach (var expectedPlaylist in expectedPlaylists)
        {
            Assert.Contains(videoDetails.Playlists, actualPlaylist =>
                actualPlaylist.Id == expectedPlaylist.Id &&
                actualPlaylist.Name == expectedPlaylist.Name);
        }
    }

    public async Task MarkAsFinishedAsync(string videoId)
    {
        var repository = serviceProvider.GetRequiredService<IVideoRepository>();
        await repository.TryClearAiOperationsInProgressAsync(TestConstants.UploadsPlaylistId, videoId,
            AiVideoOperationFlags.Title | AiVideoOperationFlags.Description | AiVideoOperationFlags.Tags | AiVideoOperationFlags.PlaylistSuggestion,
            CancellationToken.None);
    }

    public async Task AssertWalletIsNullAsync()
    {
        using var verificationScope = serviceProvider.CreateScope();
        var databaseContext = verificationScope.ServiceProvider.GetRequiredService<TubesterDb>();

        var wallet = await databaseContext.Wallets
            .AsNoTracking()
            .SingleOrDefaultAsync(entity => entity.UserId == TestConstants.UserId);

        Assert.Null(wallet);
    }

    public async Task AssertEmptyLedger()
    {
        using var verificationScope = serviceProvider.CreateScope();
        var databaseContext = verificationScope.ServiceProvider.GetRequiredService<TubesterDb>();


        var ledgerEntries = await databaseContext.LedgerEntries
            .AsNoTracking()
            .Where(entry => entry.UserId == TestConstants.UserId)
            .ToListAsync();
        Assert.NotNull(ledgerEntries);
        Assert.Empty(ledgerEntries);
    }

    public async Task AssertWalletAsync(int cost)
    {
        using var verificationScope = serviceProvider.CreateScope();
        var databaseContext = verificationScope.ServiceProvider.GetRequiredService<TubesterDb>();

        var wallet = await databaseContext.Wallets
            .AsNoTracking()
            .SingleOrDefaultAsync(entity => entity.UserId == TestConstants.UserId);

        Assert.NotNull(wallet);
        Assert.Equal(TestConstants.MonthlyCredits - cost, wallet.Balance);
    }

    public async Task AssertLedgerAfterDeductionAsync(string actionType, int cost, string expectedReferenceId,
        DateTimeOffset? expectedGrantAt = null)
    {
        expectedGrantAt ??= TestFixture.TestingDateTimeOffset;

        using var verificationScope = serviceProvider.CreateScope();
        var databaseContext = verificationScope.ServiceProvider.GetRequiredService<TubesterDb>();

        var ledgerEntries = await databaseContext.LedgerEntries
            .AsNoTracking()
            .Where(entry => entry.UserId == TestConstants.UserId)
            .OrderBy(entry => entry.OccurredAtUtc)
            .ToListAsync();

        Assert.Equal(2, ledgerEntries.Count);

        await AssertWalletAsync(cost);

        var grantEntry = Assert.Single(ledgerEntries, entry => entry.Delta > 0);
        Assert.Equal("PeriodGrant", grantEntry.ActionType);
        Assert.Equal(expectedGrantAt, grantEntry.OccurredAtUtc);
        Assert.Equal(TestConstants.MonthlyCredits, grantEntry.Delta);

        var spendEntry = Assert.Single(ledgerEntries, entry => entry.Delta < 0);
        Assert.Equal(actionType, spendEntry.ActionType);
        Assert.Equal(-cost, spendEntry.Delta);
        Assert.Equal(expectedReferenceId, spendEntry.ReferenceId);
    }

    public async Task AssertLedgerAfterBatchDeductionAsync(bool title, bool description, bool tags, bool playlist,
        string expectedReferenceId, DateTimeOffset? expectedGrantAt = null)
    {
        expectedGrantAt ??= TestFixture.TestingDateTimeOffset;

        using var verificationScope = serviceProvider.CreateScope();
        var databaseContext = verificationScope.ServiceProvider.GetRequiredService<TubesterDb>();

        var ledgerEntries = await databaseContext.LedgerEntries
            .AsNoTracking()
            .Where(entry => entry.UserId == TestConstants.UserId)
            .OrderBy(entry => entry.OccurredAtUtc)
            .ToListAsync();

        var grantEntry = Assert.Single(ledgerEntries, entry => entry.Delta > 0);
        Assert.Equal("PeriodGrant", grantEntry.ActionType);
        Assert.Equal(expectedGrantAt, grantEntry.OccurredAtUtc);
        Assert.Equal(TestConstants.MonthlyCredits, grantEntry.Delta);

        var expectedLedgerEntries = 1;
        if (title)
        {
            ++expectedLedgerEntries;
            var spendEntry = Assert.Single(ledgerEntries, entry => entry is { Delta: < 0, ActionType: nameof(CreditActionType.AiTitleEnqueued) });
            Assert.Equal(-TestConstants.AiTitleEnqueuedCost, spendEntry.Delta);
            Assert.Equal(expectedReferenceId, spendEntry.ReferenceId);
        }
        if (description)
        {
            ++expectedLedgerEntries;
            var spendEntry = Assert.Single(ledgerEntries, entry => entry is { Delta: < 0, ActionType: nameof(CreditActionType.AiDescriptionEnqueued) });
            Assert.Equal(-TestConstants.AiDescriptionEnqueuedCost, spendEntry.Delta);
            Assert.Equal(expectedReferenceId, spendEntry.ReferenceId);
        }
        if (tags)
        {
            ++expectedLedgerEntries;
            var spendEntry = Assert.Single(ledgerEntries, entry => entry is { Delta: < 0, ActionType: nameof(CreditActionType.AiTagsEnqueued) });
            Assert.Equal(-TestConstants.AiTagsEnqueuedCost, spendEntry.Delta);
            Assert.Equal(expectedReferenceId, spendEntry.ReferenceId);
        }
        if (playlist)
        {
            ++expectedLedgerEntries;
            var spendEntry = Assert.Single(ledgerEntries, entry => entry is { Delta: < 0, ActionType: nameof(CreditActionType.AiPlaylistSuggestionEnqueued) });
            Assert.Equal(-TestConstants.AiTitleEnqueuedCost, spendEntry.Delta);
            Assert.Equal(expectedReferenceId, spendEntry.ReferenceId);
        }

        Assert.Equal(expectedLedgerEntries, ledgerEntries.Count);

    }

    public static void SetProperty<TValue>(object target, string propertyName, TValue? value)
    {
        var property = target.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ?? throw new InvalidOperationException(
                $"Property '{propertyName}' was not found on type '{target.GetType().Name}'.");

        if (!property.CanWrite)
        {
            throw new InvalidOperationException(
                $"Property '{propertyName}' on type '{target.GetType().Name}' is not writable.");
        }

        property.SetValue(target, value);
    }

    public static AiTextGenerationResult CreateMockResult(string jsonResponse)
    {
        return new AiTextGenerationResult(
            jsonResponse,
            new AiUsage(
                Provider: AiProviders.Ollama,
                Model: "qwen3:8b",
                PromptTokens: 100,
                CompletionTokens: 50,
                TotalTokens: 150,
                MaxOutputTokens: 1024,
                Temperature: 0.7,
                Duration: TimeSpan.FromMilliseconds(500)));
    }

    public async Task ResetDbAsync()
    {
        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TubesterDb>();
        await PostgresCleaner.CleanAsync(dbContext.Database.GetDbConnection());
    }

    public static async IAsyncEnumerable<T> CreateAsyncEnumerable<T>(IEnumerable<T> items)
    {
        foreach (var item in items)
        {
            yield return item;
        }

        await Task.CompletedTask;
    }
}

public sealed record VideoTestDataResult(
    User User,
    Channel Channel,
    Plan? Plan = null,
    Subscription? Subscription = null,
    Video? Video = null,
    ChannelSettings? ChannelSettings = null
    );