using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tubester.Abstractions.Users;
using Tubester.Application.Contracts.Replies;
using Tubester.Domain;
using Tubester.IntegrationTests.TestHost;
using Tubester.Persistence;
using Xunit;

namespace Tubester.IntegrationTests.Replies;

[Collection(nameof(TestCollection))]
public class BatchIgnoreTests(TestFixture fixture)
{
    [Fact]
    public async Task BatchIgnore_ValidCommentIds_ReturnsSuccessResult()
    {
        // Arrange
        await fixture.CleanStateAsync();

        const string testChannelId = "batch-ignore-channel";
        const string testUploadsPlaylistId = "PLBatchIgnore";

        var user = User.Create(
            MockAuthenticationExtensions.TestSub,
            MockAuthenticationExtensions.TestEmail,
            MockAuthenticationExtensions.TestName,
            MockAuthenticationExtensions.TestPicture,
            TestFixture.TestingDateTimeOffset);

        var channel = Channel.Create(testChannelId, MockAuthenticationExtensions.TestSub, "Test Channel",
            testUploadsPlaylistId, DateTimeOffset.UtcNow);

        var video = Video.Create(
            testUploadsPlaylistId,
            "video1",
            "Test Video",
            "Description",
            TestFixture.TestingDateTimeOffset,
            TimeSpan.FromMinutes(10),
            VideoVisibility.Public,
            ["test"],
            "22",
            "en",
            "en",
            TestFixture.TestingDateTimeOffset,
            "etag1"
        );

        var reply1 = Reply.Create(
            "comment1",
            "video1",
            "Test Video",
            "First comment",
            TestFixture.TestingDateTimeOffset,
            TestFixture.TestingDateTimeOffset.AddDays(-1));

        var reply2 = Reply.Create(
            "comment2",
            "video1",
            "Test Video",
            "Second comment",
            TestFixture.TestingDateTimeOffset,
            TestFixture.TestingDateTimeOffset.AddDays(-1));

        var postedReply = Reply.Create(
            "comment3",
            "video1",
            "Test Video",
            "Posted comment",
            TestFixture.TestingDateTimeOffset,
            TestFixture.TestingDateTimeOffset.AddDays(-1));
        postedReply.SuggestText("Some text", TestFixture.TestingDateTimeOffset.AddMinutes(5));
        postedReply.ApproveText(MockAuthenticationExtensions.TestSub, "Final text", TestFixture.TestingDateTimeOffset.AddMinutes(10));
        postedReply.Post(MockAuthenticationExtensions.TestSub, TestFixture.TestingDateTimeOffset.AddMinutes(15));

        using (var scope = fixture.ApiServices.CreateScope())
        {
            var databaseContext = scope.ServiceProvider.GetRequiredService<TubesterDb>();
            databaseContext.Users.Add(user);
            databaseContext.Channels.Add(channel);
            databaseContext.Videos.Add(video);
            databaseContext.Replies.AddRange(reply1, reply2, postedReply);
            await databaseContext.SaveChangesAsync();
        }

        var commentIds = new[] { "comment1", "comment2", "comment3", "non-existent" };
        var content = TestHelpers.CreateJsonContent(commentIds);

        // Act
        var response = await fixture.HttpClient.PostAsync("/api/replies/batch-ignore", content);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await TestHelpers.DeserializeAsync<BatchIgnoreResult>(response);

        Assert.NotNull(result);
        Assert.Equal(4, result.Requested);
        Assert.Equal(2, result.Ignored); // comment1 and comment2
        Assert.Equal(0, result.AlreadyIgnored);
        Assert.Equal(1, result.SkippedPosted); // comment3
        Assert.Equal(1, result.NotFound); // non-existent
        Assert.Equal(2, result.IgnoredIds.Length);
        Assert.Contains("comment1", result.IgnoredIds);
        Assert.Contains("comment2", result.IgnoredIds);
        Assert.Single(result.SkippedPostedIds);
        Assert.Contains("comment3", result.SkippedPostedIds);
        Assert.Single(result.NotFoundIds);
        Assert.Contains("non-existent", result.NotFoundIds);

        // Verify replies were ignored in database
        using var verificationScope = fixture.ApiServices.CreateScope();
        var verificationDatabaseContext = verificationScope.ServiceProvider.GetRequiredService<TubesterDb>();
        var ignoredReplies = await verificationDatabaseContext.Replies
            .Where(r => r.CommentId == "comment1" || r.CommentId == "comment2")
            .ToListAsync();

        Assert.Equal(2, ignoredReplies.Count);
        Assert.All(ignoredReplies, r => Assert.Equal(ReplyStatus.Ignored, r.Status));
    }

    [Fact]
    public async Task BatchIgnore_EmptyCommentIds_ReturnsBadRequest()
    {
        // Arrange
        await fixture.CleanStateAsync();

        var commentIds = Array.Empty<string>();
        var content = TestHelpers.CreateJsonContent(commentIds);

        // Act
        var response = await fixture.HttpClient.PostAsync("/api/replies/batch-ignore", content);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var responseContent = await response.Content.ReadAsStringAsync();
        Assert.Contains("CommentIds cannot be empty", responseContent);
    }
}
