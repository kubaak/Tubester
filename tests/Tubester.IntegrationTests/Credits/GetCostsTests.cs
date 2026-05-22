using System.Net;
using Tubester.Api;
using Tubester.IntegrationTests.TestHost;
using Xunit;

namespace Tubester.IntegrationTests.Credits;

[Collection(nameof(TestCollection))]
public sealed class GetCostsTests(TestFixture fixture)
{
    private readonly TestHelpers _helpers = new(fixture.ApiServices);

    [Fact]
    public async Task ReturnsEnabledActionCosts()
    {
        // Arrange
        await fixture.CleanStateAsync();

        await _helpers.SeedTestDataAsync();

        // Act
        var response = await fixture.HttpClient.GetAsync("/api/credits/costs");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await TestHelpers.DeserializeAsync<CreditsController.CreditActionCosts>(response);

        Assert.NotNull(result);

        Assert.Equal(TestConstants.CopyTemplateExecutedCost, result.CopyTemplateExecuted);
        Assert.Equal(TestConstants.AiTemplateSubmittedCost, result.VideoDetailsSubmitted);
        Assert.Equal(TestConstants.AiReplyGeneratedCost, result.AiReplyGenerated);
        Assert.Equal(TestConstants.ReplyPostedActionCost, result.ReplyPostedToYouTube);

        Assert.Equal(TestConstants.AiTitleEnqueuedCost, result.AiTitle);
        Assert.Equal(TestConstants.AiDescriptionEnqueuedCost, result.AiDescription);
        Assert.Equal(TestConstants.AiTagsEnqueuedCost, result.AiTags);
        Assert.Equal(TestConstants.AiPlaylistSuggestionEnqueuedCost, result.AiPlaylist);
    }
}
