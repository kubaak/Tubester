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

        var result = await TestHelpers.DeserializeAsync<IReadOnlyList<CreditsController.CreditActionCostResponse>>(response);

        Assert.NotNull(result);
        Assert.NotEmpty(result);

        Assert.Contains(result, cost =>
            cost.ActionType == "AiTemplateEnqueued" &&
            cost.Cost == TestConstants.AiTemplateCost);

        Assert.Contains(result, cost =>
            cost.ActionType == "AiPlaylistSuggestionEnqueued" &&
            cost.Cost == TestConstants.AiPlaylistSuggestionCost);

        Assert.Contains(result, cost =>
            cost.ActionType == "AiTemplateSubmitted" &&
            cost.Cost == TestConstants.VideoDetailsSubmitActionCost);

        Assert.Contains(result, cost =>
            cost.ActionType == "AiReplyGenerated" &&
            cost.Cost == TestConstants.AiReplyGeneratedCost);

        Assert.Contains(result, cost =>
            cost.ActionType == "ReplyPostedToYouTube" &&
            cost.Cost == TestConstants.ReplyPostedActionCost);

        Assert.Contains(result, cost =>
            cost.ActionType == "CopyTemplateExecuted" &&
            cost.Cost == TestConstants.CopyTemplateExecutedCost);
    }
}
