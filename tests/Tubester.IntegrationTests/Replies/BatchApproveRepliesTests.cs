using System.Net;
using Google.Apis.YouTube.v3;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Tubester.Abstractions.Credits;
using Tubester.Application.Contracts.Replies;
using Tubester.Domain;
using Tubester.IntegrationTests.TestHost;
using Tubester.Persistence;
using Xunit;

namespace Tubester.IntegrationTests.Replies;

[Collection(nameof(TestCollection))]
public class BatchApproveTests(TestFixture fixture)
{
    private readonly TestHelpers _helpers = new(fixture.ApiServices);

    private const string OperationId = "replies-idempotency-operation";


    [Fact]
    public async Task BatchApprove_ValidDecisions_CallsYoutubeService_AndLogsAnalytics()
    {
        // Arrange
        await fixture.CleanStateAsync();

        var reply1 = Reply.Create(
            "comment1",
            TestConstants.TargetVideoId,
            "Test Video",
            "First comment",
            TestFixture.TestingDateTimeOffset,
            TestFixture.TestingDateTimeOffset.AddDays(-1));
        reply1.SuggestText("Suggested text 1", TestFixture.TestingDateTimeOffset.AddMinutes(5));

        var reply2 = Reply.Create(
            "comment2",
            TestConstants.TargetVideoId,
            "Test Video",
            "Second comment",
            TestFixture.TestingDateTimeOffset,
            TestFixture.TestingDateTimeOffset.AddDays(-1));
        reply2.SuggestText("Suggested text 2", TestFixture.TestingDateTimeOffset.AddMinutes(5));

        var options = new TestDataOptions { Replies = [reply1, reply2] };
        await _helpers.SeedTestDataAsync(options);

        var decision1 = new DraftDecisionDto { CommentId = "comment1", ApprovedText = "Approved text 1" };
        var decision2 = new DraftDecisionDto { CommentId = "comment2", ApprovedText = "Approved text 2" };

        var request = new BatchDecisionRequest([decision1, decision2]);

        var content = TestHelpers.CreateJsonContent(request);

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
        var result = await TestHelpers.DeserializeAsync<BatchDecisionResultDto>(response);

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
        await fixture.CleanStateAsync();
        fixture.ApiFactory.MockYouTubeIntegration.Reset();

        var request = new BatchDecisionRequest([]);
        var content = TestHelpers.CreateJsonContent(request);

        // Act
        var response = await fixture.HttpClient.PostAsync("/api/replies/approve", content);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
