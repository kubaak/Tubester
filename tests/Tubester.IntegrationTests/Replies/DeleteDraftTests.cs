using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tubester.IntegrationTests.TestHost;
using Tubester.Persistence;
using Xunit;

namespace Tubester.IntegrationTests.Replies;

[Collection(nameof(TestCollection))]
public class DeleteDraftTests(TestFixture fixture)
{
    private readonly TestHelpers _helpers = new(fixture.ApiServices);

    [Fact]
    public async Task DeleteDraft_ExistingReply_ReturnsOkAndDeletesFromDb()
    {
        // Arrange
        await fixture.CleanStateAsync();
        var reply = TestHelpers.GetReply("comment-to-delete");
        await _helpers.SeedTestDataAsync(new TestDataOptions
        {
            Replies = [reply],
        });

        // Act
        var response = await fixture.HttpClient.DeleteAsync($"/api/replies/{reply.CommentId}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await TestHelpers.DeserializeAsync<JsonElement>(response);

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
        await fixture.CleanStateAsync();

        // Act
        var response = await fixture.HttpClient.DeleteAsync("/api/replies/non-existent-comment");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
