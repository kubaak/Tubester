using System.Net;
using System.Text;
using System.Text.Json;
using Tubester.Application.Contracts;
using Tubester.Application.Contracts.Replies;
using Tubester.Domain;
using Tubester.IntegrationTests.TestHost;
using Xunit;

namespace Tubester.IntegrationTests;

[Collection(nameof(TestCollection))]
public class RepliesSearchTests(TestFixture fixture)
{
    private readonly TestHelpers _helpers = new(fixture);
    [Fact]
    public async Task SearchSuggestedReplies_EmptyDb_ReturnsEmptyPage()
    {
        // Arrange
        await fixture.ResetDbAsync();
        var request = new SearchSuggestedRepliesRequest();
        var json = JsonSerializer.Serialize(request, TestHelpers.SerializerOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await fixture.HttpClient.PostAsync("/api/replies/suggested/search", content);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var responseContent = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PagedResult<ReplyListItemDto>>(responseContent, TestHelpers.SerializerOptions);

        Assert.NotNull(result);
        Assert.Empty(result.Items);
        Assert.Null(result.NextPageToken);
    }

    [Fact]
    public async Task SearchSuggestedReplies_WithSuggestedReplies_ReturnsPaginatedResults()
    {
        // Arrange
        await fixture.ResetDbAsync();
        var suggestedReply1 = TestHelpers.GetReply("comment1");
        suggestedReply1.SuggestText("Suggested reply 1", TestFixture.TestingDateTimeOffset.AddMinutes(5));
        var suggestedReply2 = TestHelpers.GetReply("comment2");
        suggestedReply2.SuggestText("Suggested reply 2", TestFixture.TestingDateTimeOffset.AddMinutes(6));
        var nonSuggestedReply = TestHelpers.GetReply("comment3");
        await _helpers.SeedVideoTestDataAsync(new TestDataOptions
        {
            CreateSubscription = false,
            Replies = [suggestedReply1, suggestedReply2, nonSuggestedReply]
        });

        var request = new SearchSuggestedRepliesRequest();
        var json = JsonSerializer.Serialize(request, TestHelpers.SerializerOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await fixture.HttpClient.PostAsync("/api/replies/suggested/search", content);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var responseContent = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PagedResult<ReplyListItemDto>>(responseContent, TestHelpers.SerializerOptions);

        Assert.NotNull(result);
        Assert.Equal(2, result.Items.Count);
        Assert.Null(result.NextPageToken);
        AssertReplyListItemDto(result.Items.Single(i => i.CommentId == suggestedReply1.CommentId), suggestedReply1);
        AssertReplyListItemDto(result.Items.Single(i => i.CommentId == suggestedReply2.CommentId), suggestedReply2);
    }

    [Fact]
    public async Task SearchSuggestedReplies_FilterByVideoIds_ReturnsMatchingReplies()
    {
        // Arrange
        await fixture.ResetDbAsync();
        var video1 = TestHelpers.GetTargetVideo("video1");
        var video2 = TestHelpers.GetTargetVideo("video2");
        var reply1 = TestHelpers.GetReply("comment1", video1.VideoId, true);
        var reply2 = TestHelpers.GetReply("comment2", video2.VideoId, true);
        await _helpers.SeedVideoTestDataAsync(new TestDataOptions
        {
            Videos = [video1, video2],
            Replies = [reply1, reply2],
            CreateSubscription = false
        });

        var request = new SearchSuggestedRepliesRequest { VideoId = video1.VideoId };
        var json = JsonSerializer.Serialize(request, TestHelpers.SerializerOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await fixture.HttpClient.PostAsync("/api/replies/suggested/search", content);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var responseContent = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PagedResult<ReplyListItemDto>>(responseContent, TestHelpers.SerializerOptions);

        Assert.NotNull(result);
        Assert.Single(result.Items);
        AssertReplyListItemDto(result.Items.Single(i => i.CommentId == reply1.CommentId), reply1);
    }

    [Fact]
    public async Task SearchSuggestedReplies_FilterByOriginalComment_ReturnsMatchingReplies()
    {
        // Arrange
        await fixture.ResetDbAsync();


        var reply1 = TestHelpers.GetReply("comment1");
        reply1.SuggestText("Reply 1", TestFixture.TestingDateTimeOffset.AddMinutes(5));

        var reply2 = TestHelpers.GetReply("comment2");
        reply2.SuggestText("Reply 2", TestFixture.TestingDateTimeOffset.AddMinutes(6));

        var reply3 = TestHelpers.GetReply("comment3");
        reply3.SuggestText("Reply 3", TestFixture.TestingDateTimeOffset.AddMinutes(7));

        await _helpers.SeedVideoTestDataAsync(new TestDataOptions
        {
            Replies = [reply1, reply2, reply3],
            CreateSubscription = false
        });


        var request = new SearchSuggestedRepliesRequest { OriginalComment = "comment1" };
        var json = JsonSerializer.Serialize(request, TestHelpers.SerializerOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await fixture.HttpClient.PostAsync("/api/replies/suggested/search", content);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var responseContent = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PagedResult<ReplyListItemDto>>(responseContent, TestHelpers.SerializerOptions);

        Assert.NotNull(result);
        Assert.Single(result.Items);
        AssertReplyListItemDto(result.Items[0], reply1);
    }

    [Fact]
    public async Task SearchSuggestedReplies_Pagination_ReturnsCorrectPageAndToken()
    {
        // Arrange
        await fixture.ResetDbAsync();

        var replies = new List<Reply>();
        for (var i = 1; i <= 5; i++)
        {
            var reply = TestHelpers.GetReply($"comment{i}");
            reply.SuggestText($"Reply {i}", TestFixture.TestingDateTimeOffset.AddMinutes(i + 5));
            replies.Add(reply);
        }
        await _helpers.SeedVideoTestDataAsync(new TestDataOptions { Replies = replies });

        var request = new SearchSuggestedRepliesRequest { PageSize = 2 };
        var json = JsonSerializer.Serialize(request, TestHelpers.SerializerOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act - First page
        var response = await fixture.HttpClient.PostAsync("/api/replies/suggested/search", content);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var responseContent = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PagedResult<ReplyListItemDto>>(responseContent, TestHelpers.SerializerOptions);

        Assert.NotNull(result);
        Assert.Equal(2, result.Items.Count);
        Assert.NotNull(result.NextPageToken);
        Assert.Equal(replies[4].CommentId, result.Items[0].CommentId);
        Assert.Equal(replies[3].CommentId, result.Items[1].CommentId);

        // Act - Second page
        var request2 = new SearchSuggestedRepliesRequest { PageSize = 2, PageToken = result.NextPageToken };
        var json2 = JsonSerializer.Serialize(request2, TestHelpers.SerializerOptions);
        var content2 = new StringContent(json2, Encoding.UTF8, "application/json");

        var response2 = await fixture.HttpClient.PostAsync("/api/replies/suggested/search", content2);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);

        var responseContent2 = await response2.Content.ReadAsStringAsync();
        var result2 = JsonSerializer.Deserialize<PagedResult<ReplyListItemDto>>(responseContent2, TestHelpers.SerializerOptions);

        Assert.NotNull(result2);
        Assert.Equal(2, result2.Items.Count);
        Assert.NotNull(result2.NextPageToken);
        Assert.Equal(replies[2].CommentId, result2.Items[0].CommentId);
        Assert.Equal(replies[1].CommentId, result2.Items[1].CommentId);

        // Act - Last page
        var request3 = new SearchSuggestedRepliesRequest { PageSize = 2, PageToken = result2.NextPageToken };
        var json3 = JsonSerializer.Serialize(request3, TestHelpers.SerializerOptions);
        var content3 = new StringContent(json3, Encoding.UTF8, "application/json");

        var response3 = await fixture.HttpClient.PostAsync("/api/replies/suggested/search", content3);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response3.StatusCode);

        var responseContent3 = await response3.Content.ReadAsStringAsync();
        var result3 = JsonSerializer.Deserialize<PagedResult<ReplyListItemDto>>(responseContent3, TestHelpers.SerializerOptions);

        Assert.NotNull(result3);
        Assert.Single(result3.Items);
        Assert.Null(result3.NextPageToken); // No more pages
        Assert.Equal(replies[0].CommentId, result3.Items[0].CommentId);
    }

    [Fact]
    public async Task SearchSuggestedReplies_InvalidPageSize_ReturnsBadRequest()
    {
        // Arrange
        var request = new SearchSuggestedRepliesRequest { PageSize = 500 }; // Invalid - max is 100
        var json = JsonSerializer.Serialize(request, TestHelpers.SerializerOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await fixture.HttpClient.PostAsync("/api/replies/suggested/search", content);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static void AssertReplyListItemDto(ReplyListItemDto listItemDto, Reply reply)
    {
        Assert.Equal(reply.CommentId, listItemDto.CommentId);
        Assert.Equal(reply.VideoId, listItemDto.VideoId);
        Assert.Equal(reply.CommentText, listItemDto.CommentText);
        Assert.Equal(reply.SuggestedText, listItemDto.SuggestedText);
        Assert.Equal(reply.VideoTitle, listItemDto.VideoTitle);
        Assert.Equal(reply.ThumbnailUrl, listItemDto.ThumbnailUrl);
        Assert.Equal(reply.OriginalCommentAt, listItemDto.OriginalCommentAt);
    }
}