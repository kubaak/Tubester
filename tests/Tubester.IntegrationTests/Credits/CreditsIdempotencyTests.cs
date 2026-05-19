using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tubester.Application.Contracts.Videos;
using Tubester.IntegrationTests.TestHost;
using Tubester.Persistence;
using Xunit;

namespace Tubester.IntegrationTests.Credits;

[Collection(nameof(TestCollection))]
public sealed class CreditsIdempotencyTests(TestFixture fixture)
{
    private readonly TestHelpers _helpers = new(fixture.ApiServices);

    private const string OperationId = "credits-idempotency-operation";

    [Fact]
    public async Task AiTemplate_SameIdempotencyKey_DoesNotDoubleCharge()
    {
        // Arrange
        await fixture.CleanStateAsync();

        await _helpers.SeedTestDataAsync();
        var request = CreateAiTemplateRequest(TestConstants.TargetVideoId);

        var requestMessage1 = new HttpRequestMessage(HttpMethod.Post, "/api/videos/ai-template")
        {
            Content = TestHelpers.CreateJsonContent(request)
        };

        requestMessage1.Headers.Add("OperationId", OperationId);

        var requestMessage2 = new HttpRequestMessage(HttpMethod.Post, "/api/videos/ai-template")
        {
            Content = TestHelpers.CreateJsonContent(request)
        };

        requestMessage2.Headers.Add("OperationId", OperationId);

        // Act
        var response1 = await fixture.HttpClient.SendAsync(requestMessage1);
        //Mark as finished
        await _helpers.MarkAsFinishedAsync(TestConstants.TargetVideoId);
        var response2 = await fixture.HttpClient.SendAsync(requestMessage2);

        // Assert
        Assert.Equal(HttpStatusCode.Accepted, response1.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, response2.StatusCode);

        using var verificationScope = fixture.ApiServices.CreateScope();
        var databaseContext = verificationScope.ServiceProvider.GetRequiredService<TubesterDb>();

        var wallet = await databaseContext.Wallets
            .AsNoTracking()
            .SingleOrDefaultAsync(entity => entity.UserId == TestConstants.UserId);

        Assert.NotNull(wallet);
        Assert.Equal(TestConstants.MonthlyCredits - TestConstants.AiTemplateCost, wallet.Balance);

        var ledgerEntries = await databaseContext.LedgerEntries
            .AsNoTracking()
            .Where(entry => entry.UserId == TestConstants.UserId)
            .OrderBy(entry => entry.OccurredAtUtc)
            .ToListAsync();

        // One grant + one spend only.
        Assert.Equal(2, ledgerEntries.Count);
    }

    [Fact]
    public async Task AiTemplate_DifferentIdempotencyKey_DoubleCharge()
    {
        // Arrange
        await fixture.CleanStateAsync();
        await _helpers.SeedTestDataAsync();

        var request = CreateAiTemplateRequest(TestConstants.TargetVideoId);

        var requestMessage1 = new HttpRequestMessage(HttpMethod.Post, "/api/videos/ai-template")
        {
            Content = TestHelpers.CreateJsonContent(request)
        };

        requestMessage1.Headers.Add("OperationId", OperationId);

        var requestMessage2 = new HttpRequestMessage(HttpMethod.Post, "/api/videos/ai-template")
        {
            Content = TestHelpers.CreateJsonContent(request)
        };

        requestMessage2.Headers.Add("OperationId", "different-operation-id");

        // Act
        var response1 = await fixture.HttpClient.SendAsync(requestMessage1);
        //Mark as finished
        await _helpers.MarkAsFinishedAsync(TestConstants.TargetVideoId);
        //Act again
        var response2 = await fixture.HttpClient.SendAsync(requestMessage2);

        // Assert
        Assert.Equal(HttpStatusCode.Accepted, response1.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, response2.StatusCode);

        using var verificationScope = fixture.ApiServices.CreateScope();
        var databaseContext = verificationScope.ServiceProvider.GetRequiredService<TubesterDb>();

        var wallet = await databaseContext.Wallets
            .AsNoTracking()
            .SingleOrDefaultAsync(entity => entity.UserId == TestConstants.UserId);

        Assert.NotNull(wallet);
        Assert.Equal(TestConstants.MonthlyCredits - TestConstants.AiTemplateCost - TestConstants.AiTemplateCost, wallet.Balance);

        var ledgerEntries = await databaseContext.LedgerEntries
            .AsNoTracking()
            .Where(entry => entry.UserId == TestConstants.UserId)
            .OrderBy(entry => entry.OccurredAtUtc)
            .ToListAsync();

        // One grant + two spends.
        Assert.Equal(3, ledgerEntries.Count);
    }

    private static AiVideoTemplateRequest CreateAiTemplateRequest(string targetVideoId)
    {
        return new AiVideoTemplateRequest
        {
            TargetVideoId = targetVideoId,
            PromptEnrichment = "Generate better metadata"
        };
    }
}