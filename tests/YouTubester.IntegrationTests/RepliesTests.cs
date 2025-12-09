using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;
using YouTubester.Application.Contracts.Replies;
using YouTubester.Domain;
using YouTubester.IntegrationTests.TestHost;
using YouTubester.Persistence;

namespace YouTubester.IntegrationTests;

[Collection(nameof(TestCollection))]
public class RepliesTests(TestFixture fixture)
{
    private readonly JsonSerializerOptions _serializerOptions =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    [Fact]
    public async Task GetReplies_EmptyDb_ReturnsEmptyList()
    {
        // Arrange
        await fixture.ResetDbAsync();

        // Act
        var response = await fixture.HttpClient.GetAsync("/api/replies");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        Assert.Equal("[]", content);
    }

    [Fact]
    public async Task GetReplies_WithRepliesInDb_ReturnsRepliesForApproval()
    {
        // Arrange
        await fixture.ResetDbAsync();

        var suggestedReply = Reply.Create(
            "comment1",
            "video1",
            "Test Video 1",
            "Original comment text",
            TestFixture.TestingDateTimeOffset);
        suggestedReply.SuggestText("Suggested reply text", TestFixture.TestingDateTimeOffset.AddMinutes(5));

        var pulledReply = Reply.Create(
            "comment2",
            "video1",
            "Test Video 1",
            "Another comment text",
            TestFixture.TestingDateTimeOffset);

        var postedReply = Reply.Create(
            "comment3",
            "video2",
            "Test Video 2",
            "Posted comment text",
            TestFixture.TestingDateTimeOffset);
        postedReply.SuggestText("Some reply", TestFixture.TestingDateTimeOffset.AddMinutes(5));
        postedReply.ApproveText("Final reply", TestFixture.TestingDateTimeOffset.AddMinutes(10));
        postedReply.Post(TestFixture.TestingDateTimeOffset.AddMinutes(15));

        using (var scope = fixture.ApiServices.CreateScope())
        {
            var databaseContext = scope.ServiceProvider.GetRequiredService<YouTubesterDb>();
            databaseContext.Replies.AddRange(suggestedReply, pulledReply, postedReply);
            await databaseContext.SaveChangesAsync();
        }

        // Act
        var response = await fixture.HttpClient.GetAsync("/api/replies");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement[]>(content, _serializerOptions);

        Assert.NotNull(result);
        Assert.Single(result!); // Only Suggested replies are returned for approval

        var comment1 = result.FirstOrDefault(r => r.GetProperty("commentId").GetString() == "comment1");
        Assert.NotEqual(JsonValueKind.Undefined, comment1.ValueKind);
        Assert.Equal((int)ReplyStatus.Suggested, comment1.GetProperty("status").GetInt32());

        // comment2 (Pulled status) should not be returned for approval
        Assert.DoesNotContain(result, r => r.GetProperty("commentId").GetString() == "comment2");
        Assert.DoesNotContain(result, r =>
            r.GetProperty("commentId").GetString() == "comment3"); // Posted replies should not be returned
    }

    [Fact]
    public async Task DeleteDraft_ExistingReply_ReturnsOkAndDeletesFromDb()
    {
        // Arrange
        await fixture.ResetDbAsync();

        var reply = Reply.Create(
            "comment-to-delete",
            "video1",
            "Test Video",
            "Comment to be deleted",
            TestFixture.TestingDateTimeOffset);

        using (var scope = fixture.ApiServices.CreateScope())
        {
            var databaseContext = scope.ServiceProvider.GetRequiredService<YouTubesterDb>();
            databaseContext.Replies.Add(reply);
            await databaseContext.SaveChangesAsync();
        }

        // Act
        var response = await fixture.HttpClient.DeleteAsync("/api/replies/comment-to-delete");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(content, _serializerOptions);

        Assert.NotEqual(JsonValueKind.Undefined, result.ValueKind);
        Assert.Equal("comment-to-delete", result.GetProperty("commentId").GetString());

        // Verify reply was deleted from database
        using var verificationScope = fixture.ApiServices.CreateScope();
        var verificationDatabaseContext = verificationScope.ServiceProvider.GetRequiredService<YouTubesterDb>();
        var deletedReply = await verificationDatabaseContext.Replies
            .FirstOrDefaultAsync(r => r.CommentId == "comment-to-delete");

        Assert.Null(deletedReply);
    }

    [Fact]
    public async Task DeleteDraft_NonExistentReply_ReturnsNotFound()
    {
        // Arrange
        await fixture.ResetDbAsync();

        // Act
        var response = await fixture.HttpClient.DeleteAsync("/api/replies/non-existent-comment");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task BatchApprove_ValidDecisions_CallsYoutubeService()
    {
        // Arrange
        await fixture.ResetDbAsync();

        var reply1 = Reply.Create(
            "comment1",
            "video1",
            "Test Video",
            "First comment",
            TestFixture.TestingDateTimeOffset);
        reply1.SuggestText("Suggested text 1", TestFixture.TestingDateTimeOffset.AddMinutes(5));

        var reply2 = Reply.Create(
            "comment2",
            "video1",
            "Test Video",
            "Second comment",
            TestFixture.TestingDateTimeOffset);
        reply2.SuggestText("Suggested text 2", TestFixture.TestingDateTimeOffset.AddMinutes(5));

        using (var scope = fixture.ApiServices.CreateScope())
        {
            var databaseContext = scope.ServiceProvider.GetRequiredService<YouTubesterDb>();
            databaseContext.Replies.AddRange(reply1, reply2);
            await databaseContext.SaveChangesAsync();
        }

        var decision1 = new DraftDecisionDto("comment1", "Approved text 1");
        var decision2 = new DraftDecisionDto("comment2", "Approved text 2");

        var decisions = new[] { decision1, decision2 };

        var json = JsonSerializer.Serialize(decisions, _serializerOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var sequence = new MockSequence();

        fixture.ApiFactory.MockYouTubeIntegration
            .InSequence(sequence)
            .Setup(x => x.ReplyAsync(
                reply1.CommentId,
                decision1.ApprovedText,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        fixture.ApiFactory.MockYouTubeIntegration
            .InSequence(sequence)
            .Setup(x => x.ReplyAsync(
                reply2.CommentId,
                decision2.ApprovedText,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var response = await fixture.HttpClient.PostAsync("/api/replies/approve", content);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var responseContent = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<BatchDecisionResultDto>(responseContent, _serializerOptions);

        Assert.NotNull(result);
        Assert.Equal(2, result!.Total);
        Assert.Equal(2, result.Succeeded);
        Assert.Equal(0, result.Failed);
        Assert.Equal(2, result.Items.Count);

        // Verify replies were approved in database
        using var verificationScope = fixture.ApiServices.CreateScope();
        var verificationDatabaseContext = verificationScope.ServiceProvider.GetRequiredService<YouTubesterDb>();
        var approvedReplies = await verificationDatabaseContext.Replies
            .Where(r => r.CommentId == "comment1" || r.CommentId == "comment2")
            .ToListAsync();

        Assert.Equal(2, approvedReplies.Count);
        Assert.All(approvedReplies, r => Assert.Equal(ReplyStatus.Posted, r.Status));
        Assert.Contains(approvedReplies,
            r => r.CommentId == "comment1" && r.FinalText == decision1.ApprovedText);
        Assert.Contains(approvedReplies,
            r => r.CommentId == "comment2" && r.FinalText == decision2.ApprovedText);

        fixture.ApiFactory.MockYouTubeIntegration.VerifyAll();
    }

    [Fact]
    public async Task BatchApprove_EmptyDecisions_ReturnsBadRequest()
    {
        // Arrange
        await fixture.ResetDbAsync();
        fixture.ApiFactory.MockYouTubeIntegration.Reset();

        var decisions = Array.Empty<DraftDecisionDto>();
        var json = JsonSerializer.Serialize(decisions, _serializerOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await fixture.HttpClient.PostAsync("/api/replies/approve", content);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task BatchIgnore_ValidCommentIds_ReturnsSuccessResult()
    {
        // Arrange
        await fixture.ResetDbAsync();

        var reply1 = Reply.Create(
            "comment1",
            "video1",
            "Test Video",
            "First comment",
            TestFixture.TestingDateTimeOffset);

        var reply2 = Reply.Create(
            "comment2",
            "video1",
            "Test Video",
            "Second comment",
            TestFixture.TestingDateTimeOffset);

        var postedReply = Reply.Create(
            "comment3",
            "video1",
            "Test Video",
            "Posted comment",
            TestFixture.TestingDateTimeOffset);
        postedReply.SuggestText("Some text", TestFixture.TestingDateTimeOffset.AddMinutes(5));
        postedReply.ApproveText("Final text", TestFixture.TestingDateTimeOffset.AddMinutes(10));
        postedReply.Post(TestFixture.TestingDateTimeOffset.AddMinutes(15));

        using (var scope = fixture.ApiServices.CreateScope())
        {
            var databaseContext = scope.ServiceProvider.GetRequiredService<YouTubesterDb>();
            databaseContext.Replies.AddRange(reply1, reply2, postedReply);
            await databaseContext.SaveChangesAsync();
        }

        var commentIds = new[] { "comment1", "comment2", "comment3", "non-existent" };
        var json = JsonSerializer.Serialize(commentIds, _serializerOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await fixture.HttpClient.PostAsync("/api/replies/batch-ignore", content);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var responseContent = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<BatchIgnoreResult>(responseContent, _serializerOptions);

        Assert.NotNull(result);
        Assert.Equal(4, result!.Requested);
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
        var verificationDatabaseContext = verificationScope.ServiceProvider.GetRequiredService<YouTubesterDb>();
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
        await fixture.ResetDbAsync();

        var commentIds = Array.Empty<string>();
        var json = JsonSerializer.Serialize(commentIds, _serializerOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await fixture.HttpClient.PostAsync("/api/replies/batch-ignore", content);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        ;

        var responseContent = await response.Content.ReadAsStringAsync();
        Assert.Contains("CommentIds cannot be empty", responseContent);
    }
}