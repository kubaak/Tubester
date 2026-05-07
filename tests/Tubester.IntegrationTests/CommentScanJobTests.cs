using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Tubester.Application.Jobs;
using Tubester.Domain;
using Tubester.Integration.Dtos;
using Tubester.IntegrationTests.TestHost;
using Xunit;

namespace Tubester.IntegrationTests;

[Collection(nameof(TestCollection))]
public class CommentScanJobTests(TestFixture fixture)
{
    private readonly TestHelpers _helpers = new(fixture);

    private const string SuggestedReplyText = "Thanks for watching! 🙌";
    private const string EmojiOnlyComment = "🎉🎊✨";
    private const string CustomEmojiResponse = "🔥💪";
    private const string SimulatedAiClientFailureMessage = "Simulated AI client failure";

    private const string CommentId1 = "comment-id-1";
    private const string CommentId2 = "comment-id-2";
    private const string CommentId3 = "comment-id-3";
    private const string RecentCommentId = "comment-recent";
    private const string OldCommentId = "comment-old";
    private const string PrivateVideoId = "private-video-id";

    private const string Author1 = "author-1";
    private const string Author2 = "author-2";
    private const string Author3 = "author-3";

    private const string GreatVideoComment = "Great video!";
    private const string LoveItComment = "Love it!";
    private const string RegularComment = "Regular comment";
    private const string CommentText1 = "Comment 1";
    private const string CommentText2 = "Comment 2";
    private const string CommentText3 = "Comment 3";
    private const string RecentComment = "Recent comment";
    private const string OldComment = "Old comment";
    private const string AlreadyClaimedComment = "Already claimed comment";
    private const string SkippedComment = "This will be skipped";

    private const string NonExistentChannelId = "non-existent-channel";
    private const string DefaultLanguage = "English";

    private const int DefaultMaxSuggestions = 10;
    private const int DefaultMaxCommentAgeDays = 10;
    private const int LimitedMaxSuggestions = 2;
    private const int MaxCommentAgeDays = 5;
    private const int ZeroMonthlyCredits = 0;

    private const int RecentCommentAgeDays = -1;
    private const int OlderCommentAgeDays = -2;
    private const int TooOldCommentAgeDays = -10;

    [Fact]
    public async Task Run_WithValidComments_GeneratesSuggestedReplies()
    {
        // Arrange
        await fixture.ResetDbAsync();
        fixture.WorkerFactory.MockBackgroundYoutubeIntegration.Invocations.Clear();
        fixture.WorkerFactory.MockAiClient.Invocations.Clear();

        var targetVideo = TestHelpers.GetTargetVideo();
        await _helpers.SeedVideoTestDataAsync(new TestDataOptions
        {
            Videos = [targetVideo]
        });

        var commentThreads = new List<CommentThreadDto>
        {
            new(
                CommentId1,
                targetVideo.VideoId,
                Author1,
                GreatVideoComment,
                TestFixture.TestingDateTimeOffset.AddDays(RecentCommentAgeDays)),
            new(
                CommentId2,
                targetVideo.VideoId,
                Author2,
                LoveItComment,
                TestFixture.TestingDateTimeOffset.AddDays(OlderCommentAgeDays))
        };

        fixture.WorkerFactory.MockBackgroundYoutubeIntegration
            .Setup(x => x.GetUnansweredTopLevelCommentsAsync(
                TestConstants.ChannelId,
                targetVideo.VideoId,
                It.IsAny<CancellationToken>()))
            .Returns(commentThreads.ToAsyncEnumerable());

        fixture.WorkerFactory.MockAiClient
            .Setup(x => x.SuggestReplyAsync(
                It.IsAny<string>(),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(SuggestedReplyText);

        // Act
        using var jobScope = fixture.WorkerServices.CreateScope();
        var commentScanJob = jobScope.ServiceProvider.GetRequiredService<CommentScanJob>();
        await commentScanJob.Run(TestConstants.ChannelId, new Hangfire.JobCancellationToken(false));

        // Assert
        fixture.WorkerFactory.MockAiClient.Verify(
            x => x.SuggestReplyAsync(
                It.IsAny<string>(),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));

        var expectedReply1 = Reply.Create(
            CommentId1,
            targetVideo.VideoId,
            targetVideo.Title ?? string.Empty,
            GreatVideoComment,
            TestFixture.TestingDateTimeOffset,
            TestFixture.TestingDateTimeOffset.AddDays(RecentCommentAgeDays));
        expectedReply1.SuggestText(SuggestedReplyText, TestFixture.TestingDateTimeOffset);
        await _helpers.AssertReplyAsync(expectedReply1);

        var expectedReply2 = Reply.Create(
            CommentId2,
            targetVideo.VideoId,
            targetVideo.Title ?? string.Empty,
            LoveItComment,
            TestFixture.TestingDateTimeOffset,
            TestFixture.TestingDateTimeOffset.AddDays(OlderCommentAgeDays));
        expectedReply2.SuggestText(SuggestedReplyText, TestFixture.TestingDateTimeOffset);
        await _helpers.AssertReplyAsync(expectedReply2);
    }

    [Fact]
    public async Task Run_WhenChannelNotFound_ExitsEarly()
    {
        // Arrange
        await fixture.ResetDbAsync();
        fixture.WorkerFactory.MockBackgroundYoutubeIntegration.Invocations.Clear();

        // Act
        using var jobScope = fixture.WorkerServices.CreateScope();
        var commentScanJob = jobScope.ServiceProvider.GetRequiredService<CommentScanJob>();
        await commentScanJob.Run(NonExistentChannelId, new Hangfire.JobCancellationToken(false));

        // Assert
        fixture.WorkerFactory.MockBackgroundYoutubeIntegration.Verify(
            x => x.GetUnansweredTopLevelCommentsAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Run_WhenCommentAssistantDisabled_ExitsEarly()
    {
        // Arrange
        await fixture.ResetDbAsync();
        fixture.WorkerFactory.MockBackgroundYoutubeIntegration.Invocations.Clear();

        var targetVideo = TestHelpers.GetTargetVideo();
        await _helpers.SeedVideoTestDataAsync(new TestDataOptions
        {
            Videos = [targetVideo],
            EnableCommentScan = false
        });

        // Act
        using var jobScope = fixture.WorkerServices.CreateScope();
        var commentScanJob = jobScope.ServiceProvider.GetRequiredService<CommentScanJob>();
        await commentScanJob.Run(TestConstants.ChannelId, new Hangfire.JobCancellationToken(false));

        // Assert
        fixture.WorkerFactory.MockBackgroundYoutubeIntegration.Verify(
            x => x.GetUnansweredTopLevelCommentsAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Run_WhenCommentIsEmojiOnly_UsesConfiguredResponse()
    {
        // Arrange
        await fixture.ResetDbAsync();
        fixture.WorkerFactory.MockBackgroundYoutubeIntegration.Invocations.Clear();
        fixture.WorkerFactory.MockAiClient.Invocations.Clear();

        var targetVideo = TestHelpers.GetTargetVideo();
        var testData = await _helpers.SeedVideoTestDataAsync(new TestDataOptions
        {
            Videos = [targetVideo]
        });
        
        var commentThreads = new List<CommentThreadDto>
        {
            new(
                CommentId1,
                targetVideo.VideoId,
                Author1,
                EmojiOnlyComment,
                TestFixture.TestingDateTimeOffset.AddDays(RecentCommentAgeDays))
        };

        fixture.WorkerFactory.MockBackgroundYoutubeIntegration
            .Setup(x => x.GetUnansweredTopLevelCommentsAsync(
                TestConstants.ChannelId,
                targetVideo.VideoId,
                It.IsAny<CancellationToken>()))
            .Returns(commentThreads.ToAsyncEnumerable());

        // Act
        using var jobScope = fixture.WorkerServices.CreateScope();
        var commentScanJob = jobScope.ServiceProvider.GetRequiredService<CommentScanJob>();
        await commentScanJob.Run(TestConstants.ChannelId, new Hangfire.JobCancellationToken(false));

        // Assert
        fixture.WorkerFactory.MockAiClient.Verify(
            x => x.SuggestReplyAsync(
                It.IsAny<string>(),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        var expectedReply = Reply.Create(
            CommentId1,
            targetVideo.VideoId,
            targetVideo.Title ?? string.Empty,
            EmojiOnlyComment,
            TestFixture.TestingDateTimeOffset,
            TestFixture.TestingDateTimeOffset.AddDays(RecentCommentAgeDays));
        expectedReply.SuggestText(testData.ChannelSettings!.ResponseForNonTextualComments!, TestFixture.TestingDateTimeOffset);
        await _helpers.AssertReplyAsync(expectedReply);
    }

    [Fact]
    public async Task Run_WhenAiClientThrows_PropagatesException()
    {
        // Arrange
        await fixture.ResetDbAsync();
        fixture.WorkerFactory.MockBackgroundYoutubeIntegration.Invocations.Clear();
        fixture.WorkerFactory.MockAiClient.Invocations.Clear();
        
        var testData = await _helpers.SeedVideoTestDataAsync();

        var commentThreads = new List<CommentThreadDto>
        {
            new(
                CommentId1,
                testData.Video!.VideoId,
                Author1,
                RegularComment,
                TestFixture.TestingDateTimeOffset.AddDays(RecentCommentAgeDays))
        };

        fixture.WorkerFactory.MockBackgroundYoutubeIntegration
            .Setup(x => x.GetUnansweredTopLevelCommentsAsync(
                testData.Channel.ChannelId,
                testData.Video!.VideoId,
                It.IsAny<CancellationToken>()))
            .Returns(commentThreads.ToAsyncEnumerable());

        fixture.WorkerFactory.MockAiClient
            .Setup(x => x.SuggestReplyAsync(
                It.IsAny<string>(),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception(SimulatedAiClientFailureMessage));

        // Act & Assert
        using var jobScope = fixture.WorkerServices.CreateScope();
        var commentScanJob = jobScope.ServiceProvider.GetRequiredService<CommentScanJob>();

        var exception = await Assert.ThrowsAsync<Exception>(() =>
            commentScanJob.Run(TestConstants.ChannelId, new Hangfire.JobCancellationToken(false)));

        Assert.Equal(SimulatedAiClientFailureMessage, exception.Message);
    }

    [Fact]
    public async Task Run_WithMaxSuggestionsLimit_StopsAfterLimit()
    {
        // Arrange
        await fixture.ResetDbAsync();
        fixture.WorkerFactory.MockBackgroundYoutubeIntegration.Invocations.Clear();
        fixture.WorkerFactory.MockAiClient.Invocations.Clear();

        var targetVideo = TestHelpers.GetTargetVideo();
        await _helpers.SeedVideoTestDataAsync(new TestDataOptions
        {
            Videos = [targetVideo]
        });

        using (var scope = fixture.ApiServices.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<Persistence.TubesterDb>();
            var channelSettings = await dbContext.ChannelSettings.FindAsync(TestConstants.ChannelId);
            channelSettings!.Apply(
                true,
                true,
                LimitedMaxSuggestions,
                DefaultMaxCommentAgeDays,
                DefaultLanguage,
                null,
                TestFixture.TestingDateTimeOffset);

            await dbContext.SaveChangesAsync();
        }

        var commentThreads = new List<CommentThreadDto>
        {
            new(
                CommentId1,
                targetVideo.VideoId,
                Author1,
                CommentText1,
                TestFixture.TestingDateTimeOffset.AddDays(RecentCommentAgeDays)),
            new(
                CommentId2,
                targetVideo.VideoId,
                Author2,
                CommentText2,
                TestFixture.TestingDateTimeOffset.AddDays(RecentCommentAgeDays)),
            new(
                CommentId3,
                targetVideo.VideoId,
                Author3,
                CommentText3,
                TestFixture.TestingDateTimeOffset.AddDays(RecentCommentAgeDays))
        };

        fixture.WorkerFactory.MockBackgroundYoutubeIntegration
            .Setup(x => x.GetUnansweredTopLevelCommentsAsync(
                TestConstants.ChannelId,
                targetVideo.VideoId,
                It.IsAny<CancellationToken>()))
            .Returns(commentThreads.ToAsyncEnumerable());

        fixture.WorkerFactory.MockAiClient
            .Setup(x => x.SuggestReplyAsync(
                It.IsAny<string>(),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(SuggestedReplyText);

        // Act
        using var jobScope = fixture.WorkerServices.CreateScope();
        var commentScanJob = jobScope.ServiceProvider.GetRequiredService<CommentScanJob>();
        await commentScanJob.Run(TestConstants.ChannelId, new Hangfire.JobCancellationToken(false));

        // Assert
        fixture.WorkerFactory.MockAiClient.Verify(
            x => x.SuggestReplyAsync(
                It.IsAny<string>(),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(LimitedMaxSuggestions));

        using var verifyScope = fixture.ApiServices.CreateScope();
        var verifyDbContext = verifyScope.ServiceProvider.GetRequiredService<Persistence.TubesterDb>();
        var replies = await verifyDbContext.Replies.ToListAsync();
        Assert.Equal(LimitedMaxSuggestions, replies.Count);
    }

    [Fact]
    public async Task Run_WithMaxCommentAgeFilter_SkipsOldComments()
    {
        // Arrange
        await fixture.ResetDbAsync();
        fixture.WorkerFactory.MockBackgroundYoutubeIntegration.Invocations.Clear();
        fixture.WorkerFactory.MockAiClient.Invocations.Clear();

        var targetVideo = TestHelpers.GetTargetVideo();
        await _helpers.SeedVideoTestDataAsync(new TestDataOptions
        {
            Videos = [targetVideo]
        });

        using (var scope = fixture.ApiServices.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<Persistence.TubesterDb>();
            var channelSettings = await dbContext.ChannelSettings.FindAsync(TestConstants.ChannelId);
            channelSettings!.Apply(
                true,
                true,
                DefaultMaxSuggestions,
                MaxCommentAgeDays,
                DefaultLanguage,
                null,
                TestFixture.TestingDateTimeOffset);

            await dbContext.SaveChangesAsync();
        }

        var commentThreads = new List<CommentThreadDto>
        {
            new(
                RecentCommentId,
                targetVideo.VideoId,
                Author1,
                RecentComment,
                TestFixture.TestingDateTimeOffset.AddDays(RecentCommentAgeDays)),
            new(
                OldCommentId,
                targetVideo.VideoId,
                Author2,
                OldComment,
                TestFixture.TestingDateTimeOffset.AddDays(TooOldCommentAgeDays))
        };

        fixture.WorkerFactory.MockBackgroundYoutubeIntegration
            .Setup(x => x.GetUnansweredTopLevelCommentsAsync(
                TestConstants.ChannelId,
                targetVideo.VideoId,
                It.IsAny<CancellationToken>()))
            .Returns(commentThreads.ToAsyncEnumerable());

        fixture.WorkerFactory.MockAiClient
            .Setup(x => x.SuggestReplyAsync(
                It.IsAny<string>(),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(SuggestedReplyText);

        // Act
        using var jobScope = fixture.WorkerServices.CreateScope();
        var commentScanJob = jobScope.ServiceProvider.GetRequiredService<CommentScanJob>();
        await commentScanJob.Run(TestConstants.ChannelId, new Hangfire.JobCancellationToken(false));

        // Assert
        fixture.WorkerFactory.MockAiClient.Verify(
            x => x.SuggestReplyAsync(
                It.IsAny<string>(),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        var expectedReply = Reply.Create(
            RecentCommentId,
            targetVideo.VideoId,
            targetVideo.Title ?? string.Empty,
            RecentComment,
            TestFixture.TestingDateTimeOffset,
            TestFixture.TestingDateTimeOffset.AddDays(RecentCommentAgeDays));
        expectedReply.SuggestText(SuggestedReplyText, TestFixture.TestingDateTimeOffset);
        await _helpers.AssertReplyAsync(expectedReply);
    }

    [Fact]
    public async Task Run_WhenCommentAlreadyClaimed_SkipsWithoutAiCall()
    {
        // Arrange
        await fixture.ResetDbAsync();
        fixture.WorkerFactory.MockBackgroundYoutubeIntegration.Invocations.Clear();
        fixture.WorkerFactory.MockAiClient.Invocations.Clear();

        var targetVideo = TestHelpers.GetTargetVideo();
        await _helpers.SeedVideoTestDataAsync(new TestDataOptions
        {
            Videos = [targetVideo]
        });

        using (var scope = fixture.ApiServices.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<Persistence.TubesterDb>();
            var channelSettings = await dbContext.ChannelSettings.FindAsync(TestConstants.ChannelId);
            channelSettings!.Apply(
                true,
                true,
                DefaultMaxSuggestions,
                DefaultMaxCommentAgeDays,
                DefaultLanguage,
                null,
                TestFixture.TestingDateTimeOffset);

            var existingReply = Reply.CreateDrafting(
                CommentId1,
                targetVideo.VideoId,
                targetVideo.Title ?? string.Empty,
                AlreadyClaimedComment,
                TestFixture.TestingDateTimeOffset,
                TestFixture.TestingDateTimeOffset.AddDays(RecentCommentAgeDays));

            await dbContext.Replies.AddAsync(existingReply);
            await dbContext.SaveChangesAsync();
        }

        var commentThreads = new List<CommentThreadDto>
        {
            new(
                CommentId1,
                targetVideo.VideoId,
                Author1,
                SkippedComment,
                TestFixture.TestingDateTimeOffset.AddDays(RecentCommentAgeDays))
        };

        fixture.WorkerFactory.MockBackgroundYoutubeIntegration
            .Setup(x => x.GetUnansweredTopLevelCommentsAsync(
                TestConstants.ChannelId,
                targetVideo.VideoId,
                It.IsAny<CancellationToken>()))
            .Returns(commentThreads.ToAsyncEnumerable());

        // Act
        using var jobScope = fixture.WorkerServices.CreateScope();
        var commentScanJob = jobScope.ServiceProvider.GetRequiredService<CommentScanJob>();
        await commentScanJob.Run(TestConstants.ChannelId, new Hangfire.JobCancellationToken(false));

        // Assert
        fixture.WorkerFactory.MockAiClient.Verify(
            x => x.SuggestReplyAsync(
                It.IsAny<string>(),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Run_SkipsNonCommentableVideos()
    {
        // Arrange
        await fixture.ResetDbAsync();
        fixture.WorkerFactory.MockBackgroundYoutubeIntegration.Invocations.Clear();
        fixture.WorkerFactory.MockAiClient.Invocations.Clear();
        
        var publicVideo = TestHelpers.GetTargetVideo(visibility: VideoVisibility.Public);
        var privateVideo = TestHelpers.GetTargetVideo("private-video-id", VideoVisibility.Private);
        var nonCommentableVideo = TestHelpers.GetTargetVideo("non-commentable-video-id", iscommentable: false);

        await _helpers.SeedVideoTestDataAsync(new TestDataOptions
        {
            Videos = [publicVideo, privateVideo, nonCommentableVideo]
        });
        
        fixture.WorkerFactory.MockBackgroundYoutubeIntegration
            .Setup(x => x.GetUnansweredTopLevelCommentsAsync(
                TestConstants.ChannelId,
                publicVideo.VideoId,
                It.IsAny<CancellationToken>()))
            .Returns(AsyncEnumerable.Empty<CommentThreadDto>());

        fixture.WorkerFactory.MockBackgroundYoutubeIntegration
            .Setup(x => x.GetUnansweredTopLevelCommentsAsync(
                TestConstants.ChannelId,
                privateVideo.VideoId,
                It.IsAny<CancellationToken>()))
            .Returns(AsyncEnumerable.Empty<CommentThreadDto>());
        
        fixture.WorkerFactory.MockBackgroundYoutubeIntegration
            .Setup(x => x.GetUnansweredTopLevelCommentsAsync(
                TestConstants.ChannelId,
                nonCommentableVideo.VideoId,
                It.IsAny<CancellationToken>()))
            .Returns(AsyncEnumerable.Empty<CommentThreadDto>());

        // Act
        using var jobScope = fixture.WorkerServices.CreateScope();
        var commentScanJob = jobScope.ServiceProvider.GetRequiredService<CommentScanJob>();
        await commentScanJob.Run(TestConstants.ChannelId, new Hangfire.JobCancellationToken(false));

        // Assert
        fixture.WorkerFactory.MockBackgroundYoutubeIntegration.Verify(
            x => x.GetUnansweredTopLevelCommentsAsync(
                TestConstants.ChannelId,
                publicVideo.VideoId,
                It.IsAny<CancellationToken>()),
            Times.Once);

        fixture.WorkerFactory.MockBackgroundYoutubeIntegration.Verify(
            x => x.GetUnansweredTopLevelCommentsAsync(
                TestConstants.ChannelId,
                privateVideo.VideoId,
                It.IsAny<CancellationToken>()),
            Times.Never);
        
        fixture.WorkerFactory.MockBackgroundYoutubeIntegration.Verify(
            x => x.GetUnansweredTopLevelCommentsAsync(
                TestConstants.ChannelId,
                privateVideo.VideoId,
                It.IsAny<CancellationToken>()),
            Times.Never);
    }
}