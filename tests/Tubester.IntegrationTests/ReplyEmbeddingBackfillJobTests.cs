using Microsoft.Extensions.DependencyInjection;
using Moq;
using Pgvector;
using Tubester.Abstractions;
using Tubester.Abstractions.Users;
using Tubester.Application.Jobs;
using Tubester.Domain;
using Tubester.IntegrationTests.TestHost;
using Xunit;

namespace Tubester.IntegrationTests;

[Collection(nameof(TestCollection))]
public class ReplyEmbeddingBackfillJobTests(TestFixture fixture)
{
    private readonly TestHelpers _helpers = new(fixture.WorkerServices);

    private const string CommentId1 = "comment-id-1";
    private const string CommentId2 = "comment-id-2";
    private const string CommentText2 = "This is amazing content";
    private const string EmptyCommentText = "";

    private const string FinalText1 = "Thanks for watching!";
    private const string FinalText2 = "We appreciate your support!";

    [Fact]
    public async Task Run_EmbedsEligiblePostedRepliesWithoutEmbeddings()
    {
        // Arrange
        await fixture.CleanStateAsync();

        var video = TestHelpers.GetTargetVideo();

        // Create posted replies without embeddings
        var reply1 = TestHelpers.GetReply(CommentId1, video.VideoId, true);
        reply1.ApproveText(TestConstants.UserId, FinalText1, TestFixture.TestingDateTimeOffset);
        reply1.Post(TestConstants.UserId, TestFixture.TestingDateTimeOffset);

        var reply2 = TestHelpers.GetReply(CommentId2, video.VideoId, true);
        reply2.ApproveText(TestConstants.UserId, FinalText2, TestFixture.TestingDateTimeOffset);
        reply2.Post(TestConstants.UserId, TestFixture.TestingDateTimeOffset);

        await _helpers.SeedTestDataAsync(new TestDataOptions
        {
            Videos = [video],
            Replies = [reply1, reply2]
        });
        // Setup mock embedding service
        fixture.WorkerFactory.MockEmbeddingService
            .Setup(x => x.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmbeddingResult(new Vector(new float[768]), "test-model"));

        // Act
        using var jobScope = fixture.WorkerServices.CreateScope();
        var job = jobScope.ServiceProvider.GetRequiredService<ReplyEmbeddingBackfillJob>();
        await job.RunAsync(TestConstants.UploadsPlaylistId, 10);

        // Assert
        reply1.SetCommentEmbedding(new Vector(new float[768]), "test-model", TestFixture.TestingDateTimeOffset);
        await _helpers.AssertReplyAsync(reply1);
        reply2.SetCommentEmbedding(new Vector(new float[768]), "test-model", TestFixture.TestingDateTimeOffset);
        await _helpers.AssertReplyAsync(reply2);
    }

    [Fact]
    public async Task Run_SchedulesAndExecutesNextBatchWhenMoreRepliesRemain()
    {
        // Arrange
        await fixture.CleanStateAsync();

        var video = TestHelpers.GetTargetVideo();

        var replies = Enumerable.Range(1, 3)
            .Select(index =>
            {
                var reply = TestHelpers.GetReply($"comment-id-{index}", video.VideoId, true);
                reply.ApproveText(TestConstants.UserId, $"Final text {index}", TestFixture.TestingDateTimeOffset);
                reply.Post(TestConstants.UserId, TestFixture.TestingDateTimeOffset);
                return reply;
            })
            .ToList();

        await _helpers.SeedTestDataAsync(new TestDataOptions
        {
            Videos = [video],
            Replies = replies
        });

        fixture.WorkerFactory.MockEmbeddingService
            .Setup(x => x.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmbeddingResult(new Vector(new float[768]), "test-model"));

        // Act
        using var jobScope = fixture.WorkerServices.CreateScope();
        var job = jobScope.ServiceProvider.GetRequiredService<ReplyEmbeddingBackfillJob>();
        await job.RunAsync(TestConstants.UploadsPlaylistId, maxComments: 3, batchSize: 2, delaySeconds: 1);

        await fixture.CapturingJobClient.RunAllAsync<ReplyEmbeddingBackfillJob>(fixture.WorkerServices);

        // Assert
        foreach (var reply in replies)
        {
            reply.SetCommentEmbedding(new Vector(new float[768]), "test-model", TestFixture.TestingDateTimeOffset);
            await _helpers.AssertReplyAsync(reply);
        }
    }

    [Fact]
    public async Task Run_DoesNotEmbedIgnoredReplies()
    {
        // Arrange
        await fixture.CleanStateAsync();

        var video = TestHelpers.GetTargetVideo();
        var reply = TestHelpers.GetReply("CommentId1", video.VideoId);
        reply.Ignore();
        await _helpers.SeedTestDataAsync(new TestDataOptions
        {
            Videos = [video],
            Replies = [reply]
        });

        fixture.WorkerFactory.MockEmbeddingService
            .Setup(x => x.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmbeddingResult(new Vector(new float[768]), "test-model"));

        // Act
        using var jobScope = fixture.WorkerServices.CreateScope();
        var job = jobScope.ServiceProvider.GetRequiredService<ReplyEmbeddingBackfillJob>();
        await job.RunAsync(TestConstants.UploadsPlaylistId, 10);

        // Assert
        await _helpers.AssertReplyAsync(reply);
    }

    [Fact]
    public async Task Run_DoesNotEmbedRepliesWithEmptyCommentText()
    {
        // Arrange
        await fixture.CleanStateAsync();

        var video = TestHelpers.GetTargetVideo();

        var reply = TestHelpers.GetReply(CommentId1, video.VideoId, true);
        TestHelpers.SetProperty(reply, "CommentText", EmptyCommentText);
        reply.ApproveText(TestConstants.UserId, FinalText1, TestFixture.TestingDateTimeOffset);
        reply.Post(TestConstants.UserId, TestFixture.TestingDateTimeOffset);

        await _helpers.SeedTestDataAsync(new TestDataOptions
        {
            Videos = [video],
            Replies = [reply]
        });

        var embedCallCount = 0;

        fixture.WorkerFactory.MockEmbeddingService
            .Setup(x => x.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => embedCallCount++)
            .ReturnsAsync(new EmbeddingResult(new Vector(new float[768]), "test-model"));

        // Act
        using var jobScope = fixture.WorkerServices.CreateScope();
        var job = jobScope.ServiceProvider.GetRequiredService<ReplyEmbeddingBackfillJob>();
        await job.RunAsync(TestConstants.UploadsPlaylistId, 10);

        // Assert
        Assert.Equal(0, embedCallCount);
    }

    [Fact]
    public async Task Run_DoesNotEmbedSuggestedReplies()
    {
        // Arrange
        await fixture.CleanStateAsync();

        var video = TestHelpers.GetTargetVideo();
        var reply = TestHelpers.GetReply(CommentId1, video.VideoId, true);

        await _helpers.SeedTestDataAsync(new TestDataOptions
        {
            Videos = [video],
            Replies = [reply]
        });
        var embedCallCount = 0;

        fixture.WorkerFactory.MockEmbeddingService
            .Setup(x => x.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => embedCallCount++)
            .ReturnsAsync(new EmbeddingResult(new Vector(new float[768]), "test-model"));

        // Act
        using var jobScope = fixture.WorkerServices.CreateScope();
        var job = jobScope.ServiceProvider.GetRequiredService<ReplyEmbeddingBackfillJob>();
        await job.RunAsync(TestConstants.UploadsPlaylistId, 10);

        // Assert
        Assert.Equal(0, embedCallCount);
    }

    [Fact]
    public async Task Run_DoesNotOverwriteExistingEmbeddings()
    {
        // Arrange
        await fixture.CleanStateAsync();

        var video = TestHelpers.GetTargetVideo();

        var existingEmbedding = new Vector(new float[768]);
        var reply = TestHelpers.GetReply(CommentId1, video.VideoId, true);
        reply.ApproveText(TestConstants.UserId, FinalText1, TestFixture.TestingDateTimeOffset);
        reply.Post(TestConstants.UserId, TestFixture.TestingDateTimeOffset);
        reply.SetCommentEmbedding(existingEmbedding, "old-model", TestFixture.TestingDateTimeOffset);
        await _helpers.SeedTestDataAsync(new TestDataOptions
        {
            Videos = [video],
            Replies = [reply]
        });

        var embedCallCount = 0;

        fixture.WorkerFactory.MockEmbeddingService
            .Setup(x => x.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => embedCallCount++)
            .ReturnsAsync(new EmbeddingResult(new Vector(new float[768]), "new-model"));

        // Act
        using var jobScope = fixture.WorkerServices.CreateScope();
        var job = jobScope.ServiceProvider.GetRequiredService<ReplyEmbeddingBackfillJob>();
        await job.RunAsync(TestConstants.UploadsPlaylistId, 10);

        // Assert
        Assert.Equal(0, embedCallCount);
        await _helpers.AssertReplyAsync(reply);
    }

    [Fact]
    public async Task Run_FiltersByUploadPlaylistId()
    {
        // Arrange
        await fixture.CleanStateAsync();

        var video = TestHelpers.GetTargetVideo();
        var otherVideo = TestHelpers.GetTargetVideo("other-video-id");

        await _helpers.SeedTestDataAsync(new TestDataOptions
        {
            Videos = [video]
        });

        // Add another channel with a different uploads playlist
        using (var scope = fixture.ApiServices.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Persistence.TubesterDb>();
            var otherUser = User.Create(
                "other-user-id",
                "mail",
                "Other User",
                "pic",
                TestFixture.TestingDateTimeOffset);

            var otherChannel = Channel.Create(
                "other-channel-id",
                "other-user-id",
                "Other Channel",
                "other-playlist-id",
                TestFixture.TestingDateTimeOffset);

            var otherVideoEntity = Video.Create(
                "other-playlist-id",
                otherVideo.VideoId,
                "Other Video",
                "Other Description",
                TestFixture.TestingDateTimeOffset.AddDays(-1),
                TimeSpan.FromMinutes(5),
                VideoVisibility.Public,
                ["other"],
                "22",
                "en",
                "en",
                TestFixture.TestingDateTimeOffset,
                "etag-other",
                true);

            await db.Users.AddAsync(otherUser);
            await db.Channels.AddAsync(otherChannel);
            await db.Videos.AddAsync(otherVideoEntity);

            var existingEmbedding = new Vector(new float[768]);
            var replyInOtherChannel = TestHelpers.GetReply(CommentId2, otherVideo.VideoId, true);
            replyInOtherChannel.ApproveText(TestConstants.UserId, FinalText2, TestFixture.TestingDateTimeOffset);
            replyInOtherChannel.Post(TestConstants.UserId, TestFixture.TestingDateTimeOffset);
            replyInOtherChannel.SetCommentEmbedding(existingEmbedding, "old-model", TestFixture.TestingDateTimeOffset);
            await db.Replies.AddAsync(replyInOtherChannel);

            await db.SaveChangesAsync();
        }

        var embedCallCount = 0;

        fixture.WorkerFactory.MockEmbeddingService
            .Setup(x => x.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((text, _) =>
            {
                if (text == CommentText2)
                {
                    embedCallCount++;
                }
            })
            .ReturnsAsync(new EmbeddingResult(new Vector(new float[768]), "test-model"));

        // Act
        using var jobScope = fixture.WorkerServices.CreateScope();
        var job = jobScope.ServiceProvider.GetRequiredService<ReplyEmbeddingBackfillJob>();
        await job.RunAsync(TestConstants.UploadsPlaylistId, 10);

        // Assert - The reply in the other channel should not be processed
        Assert.Equal(0, embedCallCount);
    }

    [Fact]
    public async Task Run_ProcessesApprovedReplies()
    {
        // Arrange
        await fixture.CleanStateAsync();

        var video = TestHelpers.GetTargetVideo();
        var reply = TestHelpers.GetReply(CommentId1, video.VideoId, true);
        reply.ApproveText(TestConstants.UserId, FinalText1, TestFixture.TestingDateTimeOffset);
        await _helpers.SeedTestDataAsync(new TestDataOptions
        {
            Videos = [video],
            Replies = [reply]
        });

        fixture.WorkerFactory.MockEmbeddingService
            .Setup(x => x.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmbeddingResult(new Vector(new float[768]), "test-model"));

        // Act
        using var jobScope = fixture.WorkerServices.CreateScope();
        var job = jobScope.ServiceProvider.GetRequiredService<ReplyEmbeddingBackfillJob>();
        await job.RunAsync(TestConstants.UploadsPlaylistId, 10);

        // Assert
        reply.SetCommentEmbedding(new Vector(new float[768]), "test-model", TestFixture.TestingDateTimeOffset);
        await _helpers.AssertReplyAsync(reply);
    }
}
