using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Tubester.Abstractions.Credits;
using Tubester.Abstractions.Users;
using Tubester.Application.Contracts;
using Tubester.Application.Contracts.Replies;
using Tubester.Domain;
using Tubester.IntegrationTests.TestHost;
using Tubester.Persistence;
using Tubester.Persistence.Credits;
using Xunit;

namespace Tubester.IntegrationTests;

[Collection(nameof(TestCollection))]
public class RepliesTests(TestFixture fixture)
{
    private readonly JsonSerializerOptions _serializerOptions =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private const string OperationId = "replies-idempotency-operation";

    [Fact]
    public async Task SearchSuggestedReplies_EmptyDb_ReturnsEmptyPage()
    {
        // Arrange
        await fixture.ResetDbAsync();

        const string testChannelId = "testChannelID123";
        const string testUploadsPlaylistId = "PLTestUploads123";
        fixture.ApiFactory.MockCurrentChannelContext.Setup(x => x.GetRequiredChannelId())
            .Returns(testChannelId);

        await SetupTestDataAsync(testChannelId, testUploadsPlaylistId);

        var request = new SearchSuggestedRepliesRequest();
        var json = JsonSerializer.Serialize(request, _serializerOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await fixture.HttpClient.PostAsync("/api/replies/suggested/search", content);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var responseContent = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PagedResult<ReplyListItemDto>>(responseContent, _serializerOptions);

        Assert.NotNull(result);
        Assert.Empty(result.Items);
        Assert.Null(result.NextPageToken);
    }

    [Fact]
    public async Task SearchSuggestedReplies_WithSuggestedReplies_ReturnsPaginatedResults()
    {
        // Arrange
        await fixture.ResetDbAsync();

        const string testChannelId = "testChannelID123";
        const string testUploadsPlaylistId = "PLTestUploads123";
        fixture.ApiFactory.MockCurrentChannelContext.Setup(x => x.GetRequiredChannelId())
            .Returns(testChannelId);

        var suggestedReply1 = Reply.Create(
            "comment1",
            "video1",
            "Test Video 1",
            "First comment text",
            TestFixture.TestingDateTimeOffset,
            TestFixture.TestingDateTimeOffset.AddDays(-1));
        suggestedReply1.SuggestText("Suggested reply 1", TestFixture.TestingDateTimeOffset.AddMinutes(5));

        var suggestedReply2 = Reply.Create(
            "comment2",
            "video1",
            "Test Video 1",
            "Second comment text",
            TestFixture.TestingDateTimeOffset.AddMinutes(1),
            TestFixture.TestingDateTimeOffset.AddDays(-1));
        suggestedReply2.SuggestText("Suggested reply 2", TestFixture.TestingDateTimeOffset.AddMinutes(6));

        var nonSuggestedReply = Reply.Create(
            "comment3",
            "video1",
            "Test Video 1",
            "Pulled comment text",
            TestFixture.TestingDateTimeOffset.AddMinutes(2),
            TestFixture.TestingDateTimeOffset.AddDays(-1));

        await SetupTestDataAsync(
            testChannelId,
            testUploadsPlaylistId,
            suggestedReply1,
            suggestedReply2,
            nonSuggestedReply);

        var request = new SearchSuggestedRepliesRequest();
        var json = JsonSerializer.Serialize(request, _serializerOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await fixture.HttpClient.PostAsync("/api/replies/suggested/search", content);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var responseContent = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PagedResult<ReplyListItemDto>>(responseContent, _serializerOptions);

        Assert.NotNull(result);
        Assert.Equal(2, result.Items.Count);
        Assert.Null(result.NextPageToken);
    }

    [Fact]
    public async Task SearchSuggestedReplies_FilterByVideoIds_ReturnsMatchingReplies()
    {
        // Arrange
        await fixture.ResetDbAsync();

        const string testChannelId = "testChannelID123";
        const string testUploadsPlaylistId = "PLTestUploads123";
        fixture.ApiFactory.MockCurrentChannelContext.Setup(x => x.GetRequiredChannelId())
            .Returns(testChannelId);

        var video1 = Video.Create(
            testUploadsPlaylistId,
            "video1",
            "Test Video 1",
            "Description 1",
            TestFixture.TestingDateTimeOffset,
            TimeSpan.FromMinutes(10),
            VideoVisibility.Public,
            ["test"],
            "22",
            "en",
            "en",
            null,
            null,
            TestFixture.TestingDateTimeOffset,
            "etag1"
        );

        var video2 = Video.Create(
            testUploadsPlaylistId,
            "video2",
            "Test Video 2",
            "Description 2",
            TestFixture.TestingDateTimeOffset.AddMinutes(1),
            TimeSpan.FromMinutes(5),
            VideoVisibility.Public,
            ["test"],
            "22",
            "en",
            "en",
            null,
            null,
            TestFixture.TestingDateTimeOffset,
            "etag2"
        );

        var reply1 = Reply.Create("comment1", "video1", "Video 1", "Comment on video 1", TestFixture.TestingDateTimeOffset,
            TestFixture.TestingDateTimeOffset.AddDays(-1));
        reply1.SuggestText("Reply 1", TestFixture.TestingDateTimeOffset.AddMinutes(5));

        var reply2 = Reply.Create("comment2", "video2", "Video 2", "Comment on video 2", TestFixture.TestingDateTimeOffset.AddMinutes(1),
            TestFixture.TestingDateTimeOffset.AddDays(-1));
        reply2.SuggestText("Reply 2", TestFixture.TestingDateTimeOffset.AddMinutes(6));

        var user = User.Create(
            MockAuthenticationExtensions.TestSub,
            MockAuthenticationExtensions.TestEmail,
            MockAuthenticationExtensions.TestName,
            MockAuthenticationExtensions.TestPicture,
            TestFixture.TestingDateTimeOffset);

        var channel = Channel.Create(testChannelId, MockAuthenticationExtensions.TestSub, "Test Channel",
            testUploadsPlaylistId, DateTimeOffset.UtcNow);

        using (var scope = fixture.ApiServices.CreateScope())
        {
            var databaseContext = scope.ServiceProvider.GetRequiredService<TubesterDb>();
            databaseContext.Users.Add(user);
            databaseContext.Channels.Add(channel);
            databaseContext.Videos.AddRange(video1, video2);
            databaseContext.Replies.AddRange(reply1, reply2);
            await databaseContext.SaveChangesAsync();
        }

        var request = new SearchSuggestedRepliesRequest { VideoId = "video1" };
        var json = JsonSerializer.Serialize(request, _serializerOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await fixture.HttpClient.PostAsync("/api/replies/suggested/search", content);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var responseContent = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PagedResult<ReplyListItemDto>>(responseContent, _serializerOptions);

        Assert.NotNull(result);
        Assert.Single(result.Items);
        Assert.Equal("comment1", result.Items[0].CommentId);
        Assert.Equal("video1", result.Items[0].VideoId);
    }

    [Fact]
    public async Task SearchSuggestedReplies_FilterByOriginalComment_ReturnsMatchingReplies()
    {
        // Arrange
        await fixture.ResetDbAsync();

        const string testChannelId = "testChannelID123";
        const string testUploadsPlaylistId = "PLTestUploads123";
        fixture.ApiFactory.MockCurrentChannelContext.Setup(x => x.GetRequiredChannelId())
            .Returns(testChannelId);

        var reply1 = Reply.Create("comment1", "video1", "Video 1", "How to cook pasta", TestFixture.TestingDateTimeOffset,
            TestFixture.TestingDateTimeOffset.AddDays(-1));
        reply1.SuggestText("Reply 1", TestFixture.TestingDateTimeOffset.AddMinutes(5));

        var reply2 = Reply.Create("comment2", "video1", "Video 1", "What ingredients needed", TestFixture.TestingDateTimeOffset.AddMinutes(1),
            TestFixture.TestingDateTimeOffset.AddDays(-1));
        reply2.SuggestText("Reply 2", TestFixture.TestingDateTimeOffset.AddMinutes(6));

        var reply3 = Reply.Create("comment3", "video1", "Video 1", "Great tutorial", TestFixture.TestingDateTimeOffset.AddMinutes(2),
            TestFixture.TestingDateTimeOffset.AddDays(-1));
        reply3.SuggestText("Reply 3", TestFixture.TestingDateTimeOffset.AddMinutes(7));

        await SetupTestDataAsync(
            testChannelId,
            testUploadsPlaylistId,
            reply1, reply2, reply3);

        var request = new SearchSuggestedRepliesRequest { OriginalComment = "cook" };
        var json = JsonSerializer.Serialize(request, _serializerOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await fixture.HttpClient.PostAsync("/api/replies/suggested/search", content);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var responseContent = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PagedResult<ReplyListItemDto>>(responseContent, _serializerOptions);

        Assert.NotNull(result);
        Assert.Single(result.Items);
        Assert.Equal("comment1", result.Items[0].CommentId);
        Assert.Contains("cook", result.Items[0].CommentText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SearchSuggestedReplies_Pagination_ReturnsCorrectPageAndToken()
    {
        // Arrange
        await fixture.ResetDbAsync();

        const string testChannelId = "testChannelID123";
        const string testUploadsPlaylistId = "PLTestUploads123";
        fixture.ApiFactory.MockCurrentChannelContext.Setup(x => x.GetRequiredChannelId())
            .Returns(testChannelId);

        var replies = new List<Reply>();
        for (var i = 1; i <= 5; i++)
        {
            var reply = Reply.Create(
                $"comment{i}",
                "video1",
                "Test Video",
                $"Comment {i}",
                TestFixture.TestingDateTimeOffset.AddMinutes(i),
                TestFixture.TestingDateTimeOffset.AddDays(-1));
            reply.SuggestText($"Reply {i}", TestFixture.TestingDateTimeOffset.AddMinutes(i + 5));
            replies.Add(reply);
        }

        await SetupTestDataAsync(
            testChannelId,
            testUploadsPlaylistId,
            replies.ToArray());

        var request = new SearchSuggestedRepliesRequest { PageSize = 2 };
        var json = JsonSerializer.Serialize(request, _serializerOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act - First page
        var response = await fixture.HttpClient.PostAsync("/api/replies/suggested/search", content);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var responseContent = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PagedResult<ReplyListItemDto>>(responseContent, _serializerOptions);

        Assert.NotNull(result);
        Assert.Equal(2, result.Items.Count);
        Assert.NotNull(result.NextPageToken);
        Assert.Equal(replies[4].CommentId, result.Items[0].CommentId);
        Assert.Equal(replies[3].CommentId, result.Items[1].CommentId);

        // Act - Second page
        var request2 = new SearchSuggestedRepliesRequest { PageSize = 2, PageToken = result.NextPageToken };
        var json2 = JsonSerializer.Serialize(request2, _serializerOptions);
        var content2 = new StringContent(json2, Encoding.UTF8, "application/json");

        var response2 = await fixture.HttpClient.PostAsync("/api/replies/suggested/search", content2);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);

        var responseContent2 = await response2.Content.ReadAsStringAsync();
        var result2 = JsonSerializer.Deserialize<PagedResult<ReplyListItemDto>>(responseContent2, _serializerOptions);

        Assert.NotNull(result2);
        Assert.Equal(2, result2.Items.Count);
        Assert.NotNull(result2.NextPageToken);
        Assert.Equal(replies[2].CommentId, result2.Items[0].CommentId);
        Assert.Equal(replies[1].CommentId, result2.Items[1].CommentId);

        // Act - Last page
        var request3 = new SearchSuggestedRepliesRequest { PageSize = 2, PageToken = result2.NextPageToken };
        var json3 = JsonSerializer.Serialize(request3, _serializerOptions);
        var content3 = new StringContent(json3, Encoding.UTF8, "application/json");

        var response3 = await fixture.HttpClient.PostAsync("/api/replies/suggested/search", content3);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response3.StatusCode);

        var responseContent3 = await response3.Content.ReadAsStringAsync();
        var result3 = JsonSerializer.Deserialize<PagedResult<ReplyListItemDto>>(responseContent3, _serializerOptions);

        Assert.NotNull(result3);
        Assert.Single(result3.Items);
        Assert.Null(result3.NextPageToken); // No more pages
        Assert.Equal(replies[0].CommentId, result3.Items[0].CommentId);
    }

    [Fact]
    public async Task SearchSuggestedReplies_InvalidPageSize_ReturnsBadRequest()
    {
        // Arrange
        await fixture.ResetDbAsync();

        const string testChannelId = "testChannelID123";
        const string testUploadsPlaylistId = "PLTestUploads123";
        fixture.ApiFactory.MockCurrentChannelContext.Setup(x => x.GetRequiredChannelId())
            .Returns(testChannelId);

        await SetupTestDataAsync(testChannelId, testUploadsPlaylistId);

        var request = new SearchSuggestedRepliesRequest { PageSize = 500 }; // Invalid - max is 100
        var json = JsonSerializer.Serialize(request, _serializerOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await fixture.HttpClient.PostAsync("/api/replies/suggested/search", content);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
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
            TestFixture.TestingDateTimeOffset,
            TestFixture.TestingDateTimeOffset.AddDays(-1));

        using (var scope = fixture.ApiServices.CreateScope())
        {
            var databaseContext = scope.ServiceProvider.GetRequiredService<TubesterDb>();
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
        var verificationDatabaseContext = verificationScope.ServiceProvider.GetRequiredService<TubesterDb>();
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
    public async Task BatchApprove_ValidDecisions_CallsYoutubeService_AndLogsAnalytics()
    {
        // Arrange
        await fixture.ResetDbAsync();
        fixture.ApiFactory.MockYouTubeIntegration.Reset();

        var reply1 = Reply.Create(
            "comment1",
            "video1",
            "Test Video",
            "First comment",
            TestFixture.TestingDateTimeOffset,
            TestFixture.TestingDateTimeOffset.AddDays(-1));
        reply1.SuggestText("Suggested text 1", TestFixture.TestingDateTimeOffset.AddMinutes(5));

        var reply2 = Reply.Create(
            "comment2",
            "video1",
            "Test Video",
            "Second comment",
            TestFixture.TestingDateTimeOffset,
            TestFixture.TestingDateTimeOffset.AddDays(-1));
        reply2.SuggestText("Suggested text 2", TestFixture.TestingDateTimeOffset.AddMinutes(5));

        using (var scope = fixture.ApiServices.CreateScope())
        {
            var databaseContext = scope.ServiceProvider.GetRequiredService<TubesterDb>();
            var plan = new Plan
            {
                Code = "FreePlan",
                Name = "Free Plan",
                MonthlyCredits = 5,
                IsActive = true,
                CreatedAtUtc = TestFixture.TestingDateTimeOffset,
                UpdatedAtUtc = TestFixture.TestingDateTimeOffset
            };

            await databaseContext.Plans.AddAsync(plan, CancellationToken.None);
            await databaseContext.SaveChangesAsync(CancellationToken.None);
            var user = User.Create(
                MockAuthenticationExtensions.TestSub,
                MockAuthenticationExtensions.TestEmail,
                MockAuthenticationExtensions.TestName,
                MockAuthenticationExtensions.TestPicture,
                TestFixture.TestingDateTimeOffset);

            await databaseContext.Users.AddAsync(user, CancellationToken.None);
            var userSubscription = new Subscription
            {
                UserId = user.Id,
                PlanId = plan.Id,
                PeriodStartUtc = TestFixture.TestingDateTimeOffset,
                PeriodEndUtc = TestFixture.TestingDateTimeOffset.AddMonths(1),
                Status = SubscriptionStatus.Active
            };
            await databaseContext.Subscriptions.AddAsync(userSubscription, CancellationToken.None);

            databaseContext.Replies.AddRange(reply1, reply2);

            var replyPostedCost = new ActionCost
            {
                ActionType = nameof(CreditActionType.ReplyPostedToYouTube),
                Cost = 0,
                IsEnabled = true,
                UpdatedAtUtc = TestFixture.TestingDateTimeOffset,
                Notes = "Integration test cost for ReplyPostedToYouTube."
            };

            await databaseContext.ActionCosts.AddAsync(replyPostedCost, CancellationToken.None);
            await databaseContext.SaveChangesAsync();
        }

        var decision1 = new DraftDecisionDto { CommentId = "comment1", ApprovedText = "Approved text 1" };
        var decision2 = new DraftDecisionDto { CommentId = "comment2", ApprovedText = "Approved text 2" };

        var request = new BatchDecisionRequest([decision1, decision2]);

        var json = JsonSerializer.Serialize(request, _serializerOptions);
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
        var requestMessage = new HttpRequestMessage(HttpMethod.Post, "/api/replies/approve")
        {
            Content = content
        };
        requestMessage.Headers.Add("OperationId", OperationId);
        var response = await fixture.HttpClient.SendAsync(requestMessage);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var responseContent = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<BatchDecisionResultDto>(responseContent, _serializerOptions);

        Assert.NotNull(result);
        Assert.Equal(2, result.Total);
        Assert.Equal(2, result.Succeeded);
        Assert.Equal(0, result.Failed);
        Assert.Equal(2, result.Items.Count);

        // Verify replies were approved in database
        using var verificationScope = fixture.ApiServices.CreateScope();
        var verificationDatabaseContext = verificationScope.ServiceProvider.GetRequiredService<TubesterDb>();
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

        // Verify ReplyPostedToYouTube analytics events are logged
        using var verifyScope = fixture.ApiServices.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<TubesterDb>();
        var events = await verifyDb.UserEvents
            .Where(e => e.EventType == nameof(CreditActionType.ReplyPostedToYouTube))
            .OrderBy(e => e.CommentId)
            .ToListAsync();

        Assert.Equal(2, events.Count);
        Assert.All(events, e => Assert.Equal(MockAuthenticationExtensions.TestSub, e.UserId));
        Assert.Equal("comment1", events[0].CommentId);
        Assert.Equal("comment2", events[1].CommentId);
        Assert.False(string.IsNullOrWhiteSpace(events[0].MetadataJson));
        Assert.False(string.IsNullOrWhiteSpace(events[1].MetadataJson));
    }

    [Fact]
    public async Task BatchApprove_EmptyDecisions_ReturnsBadRequest()
    {
        // Arrange
        await fixture.ResetDbAsync();
        fixture.ApiFactory.MockYouTubeIntegration.Reset();

        var request = new BatchDecisionRequest([]);
        var json = JsonSerializer.Serialize(request, _serializerOptions);
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
        await fixture.ResetDbAsync();

        var commentIds = Array.Empty<string>();
        var json = JsonSerializer.Serialize(commentIds, _serializerOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await fixture.HttpClient.PostAsync("/api/replies/batch-ignore", content);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var responseContent = await response.Content.ReadAsStringAsync();
        Assert.Contains("CommentIds cannot be empty", responseContent);
    }

    [Fact]
    public async Task SearchSuggestedReplies_IncludesThumbnailUrl()
    {
        // Arrange
        await fixture.ResetDbAsync();

        const string testChannelId = "thumbnail-url-channel";
        const string testUploadsPlaylistId = "PLThumbnailUploads";
        fixture.ApiFactory.MockCurrentChannelContext.Setup(x => x.GetRequiredChannelId())
            .Returns(testChannelId);

        var video = Video.Create(
            testUploadsPlaylistId,
            "testVideo123",
            "Thumbnail Test Video",
            "Testing thumbnail URL",
            TestFixture.TestingDateTimeOffset,
            TimeSpan.FromMinutes(5),
            VideoVisibility.Public,
            ["test"],
            "22",
            "en",
            "en",
            null,
            null,
            TestFixture.TestingDateTimeOffset,
            "etag-thumbnail"
        );

        var reply = Reply.Create(
            "thumbnail-comment-1",
            "testVideo123",
            "Thumbnail Test Video",
            "Test comment for thumbnail",
            TestFixture.TestingDateTimeOffset,
            TestFixture.TestingDateTimeOffset.AddDays(-1));
        reply.SuggestText("Suggested reply with thumbnail", TestFixture.TestingDateTimeOffset.AddMinutes(5));

        var user = User.Create(
            MockAuthenticationExtensions.TestSub,
            MockAuthenticationExtensions.TestEmail,
            MockAuthenticationExtensions.TestName,
            MockAuthenticationExtensions.TestPicture,
            TestFixture.TestingDateTimeOffset);

        var channel = Channel.Create(testChannelId, MockAuthenticationExtensions.TestSub, "Thumbnail Test Channel",
            testUploadsPlaylistId, DateTimeOffset.UtcNow);

        using (var scope = fixture.ApiServices.CreateScope())
        {
            var databaseContext = scope.ServiceProvider.GetRequiredService<TubesterDb>();
            databaseContext.Users.Add(user);
            databaseContext.Channels.Add(channel);
            databaseContext.Videos.Add(video);
            databaseContext.Replies.Add(reply);
            await databaseContext.SaveChangesAsync();
        }

        var request = new SearchSuggestedRepliesRequest();
        var json = JsonSerializer.Serialize(request, _serializerOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await fixture.HttpClient.PostAsync("/api/replies/suggested/search", content);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var contentResponse = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PagedResult<ReplyListItemDto>>(contentResponse, _serializerOptions);

        Assert.NotNull(result);
        Assert.Single(result.Items);

        var replyResult = result.Items[0];
        Assert.Equal("thumbnail-comment-1", replyResult.CommentId);
        Assert.Equal($"https://i.ytimg.com/vi/testVideo123/sddefault.jpg", replyResult.ThumbnailUrl);
    }

    public sealed record SearchSuggestedRepliesRequest
    {
        public string? VideoId { get; init; }
        public string? OriginalComment { get; init; }
        public int? PageSize { get; init; }
        public string? PageToken { get; init; }
    }

    private async Task<(Channel channel, Video video, User user)> SetupTestDataAsync(
        string testChannelId,
        string testUploadsPlaylistId,
        params Reply[] replies)
    {
        var user = User.Create(
            MockAuthenticationExtensions.TestSub,
            MockAuthenticationExtensions.TestEmail,
            MockAuthenticationExtensions.TestName,
            MockAuthenticationExtensions.TestPicture,
            TestFixture.TestingDateTimeOffset);

        var channel = Channel.Create(testChannelId, MockAuthenticationExtensions.TestSub, "testChannelName123",
            testUploadsPlaylistId, DateTimeOffset.UtcNow);

        var video = Video.Create(
            testUploadsPlaylistId,
            "video1",
            "Test Video 1",
            "Learn how to cook",
            TestFixture.TestingDateTimeOffset,
            TimeSpan.FromMinutes(10),
            VideoVisibility.Public,
            new[] { "cooking", "tutorial" },
            "22",
            "en",
            "en",
            null,
            null,
            TestFixture.TestingDateTimeOffset,
            "etag1"
        );

        using var scope = fixture.ApiServices.CreateScope();
        var databaseContext = scope.ServiceProvider.GetRequiredService<TubesterDb>();
        databaseContext.Users.Add(user);
        databaseContext.Channels.Add(channel);
        databaseContext.Videos.Add(video);
        if (replies.Length > 0)
        {
            databaseContext.Replies.AddRange(replies);
        }
        await databaseContext.SaveChangesAsync();

        return (channel, video, user);
    }
}
