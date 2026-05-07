using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Tubester.Abstractions.Users;
using Tubester.Api;
using Tubester.Application.Contracts.Videos;
using Tubester.IntegrationTests.TestHost;
using Tubester.Persistence;
using Tubester.Persistence.Credits;
using Xunit;

namespace Tubester.IntegrationTests;

[Collection(nameof(TestCollection))]
public sealed class CreditsControllerTests(TestFixture fixture)
{
    private readonly TestHelpers _helpers = new(fixture.ApiServices);

    [Fact]
    public async Task GetBalance_UserHasNoWallet_ReturnsZeroBalance()
    {
        // Arrange
        await fixture.CleanStateAsync();

        // Act
        var response = await fixture.HttpClient.GetAsync("/api/credits/balance");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await TestHelpers.DeserializeAsync<CreditsController.CreditBalanceResponse>(response);

        Assert.NotNull(result);
        Assert.Equal(0, result.Balance);
        Assert.Null(result.PeriodStartUtc);
        Assert.Null(result.PeriodEndUtc);
    }

    [Fact]
    public async Task GetBalance_UserHasWallet_ReturnsWalletBalance()
    {
        // Arrange
        await fixture.CleanStateAsync();

        const string userId = MockAuthenticationExtensions.TestSub;
        const int expectedBalance = 100;
        var periodStart = TestFixture.TestingDateTimeOffset;
        var periodEnd = TestFixture.TestingDateTimeOffset.AddMonths(1);

        using (var scope = fixture.ApiServices.CreateScope())
        {
            var databaseContext = scope.ServiceProvider.GetRequiredService<TubesterDb>();

            var user = User.Create(
                userId,
                MockAuthenticationExtensions.TestEmail,
                MockAuthenticationExtensions.TestName,
                MockAuthenticationExtensions.TestPicture,
                TestFixture.TestingDateTimeOffset);

            await databaseContext.Users.AddAsync(user, CancellationToken.None);

            var wallet = new Wallet
            {
                UserId = userId,
                Balance = expectedBalance,
                PeriodStartUtc = periodStart,
                PeriodEndUtc = periodEnd,
                UpdatedAtUtc = TestFixture.TestingDateTimeOffset
            };

            await databaseContext.Wallets.AddAsync(wallet, CancellationToken.None);

            await databaseContext.SaveChangesAsync(CancellationToken.None);
        }

        // Act
        var response = await fixture.HttpClient.GetAsync("/api/credits/balance");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await TestHelpers.DeserializeAsync<CreditsController.CreditBalanceResponse>(response);

        Assert.NotNull(result);
        Assert.Equal(expectedBalance, result.Balance);
        Assert.NotNull(result.PeriodStartUtc);
        Assert.NotNull(result.PeriodEndUtc);
        Assert.Equal(periodStart, result.PeriodStartUtc);
        Assert.Equal(periodEnd, result.PeriodEndUtc);
    }

    [Fact]
    public async Task GetBalance_AfterCreditGrant_ReturnsUpdatedBalance()
    {
        // Arrange
        await fixture.CleanStateAsync();

        const string userId = MockAuthenticationExtensions.TestSub;
        const int initialBalance = 50;
        const int grantAmount = 25;
        const int expectedBalance = initialBalance + grantAmount;

        using (var scope = fixture.ApiServices.CreateScope())
        {
            var databaseContext = scope.ServiceProvider.GetRequiredService<TubesterDb>();

            var user = User.Create(
                userId,
                MockAuthenticationExtensions.TestEmail,
                MockAuthenticationExtensions.TestName,
                MockAuthenticationExtensions.TestPicture,
                TestFixture.TestingDateTimeOffset);

            await databaseContext.Users.AddAsync(user, CancellationToken.None);

            var wallet = new Wallet
            {
                UserId = userId,
                Balance = initialBalance,
                PeriodStartUtc = TestFixture.TestingDateTimeOffset,
                PeriodEndUtc = TestFixture.TestingDateTimeOffset.AddMonths(1),
                UpdatedAtUtc = TestFixture.TestingDateTimeOffset
            };

            await databaseContext.Wallets.AddAsync(wallet, CancellationToken.None);

            await databaseContext.SaveChangesAsync(CancellationToken.None);
        }

        // Act - Call admin grant endpoint
        const string operationId = "balance-update-test";

        var grantRequest = new AdminCreditsController.CreditGrantRequest
        {
            UserId = userId,
            Amount = grantAmount
        };

        var grantRequestMessage = new HttpRequestMessage(HttpMethod.Post, "/api/admin/credits/grants")
        {
            Content = TestHelpers.CreateJsonContent(grantRequest)
        };

        grantRequestMessage.Headers.Add("OperationId", operationId);

        var grantResponse = await fixture.HttpClient.SendAsync(grantRequestMessage);

        Assert.Equal(HttpStatusCode.OK, grantResponse.StatusCode);

        // Act - Get balance
        var balanceResponse = await fixture.HttpClient.GetAsync("/api/credits/balance");

        // Assert
        Assert.Equal(HttpStatusCode.OK, balanceResponse.StatusCode);
        var result = await TestHelpers.DeserializeAsync<CreditsController.CreditBalanceResponse>(balanceResponse);

        Assert.NotNull(result);
        Assert.Equal(expectedBalance, result.Balance);
    }

    [Fact]
    public async Task GetBalance_AfterCreditSpend_ReturnsReducedBalance()
    {
        // Arrange
        await fixture.CleanStateAsync();
        var targetVideo = TestHelpers.GetTargetVideo();
        var testData = new TestDataOptions
        {
            Videos = [targetVideo]
        };

        await _helpers.SeedTestDataAsync(testData);

        var request = new AiVideoTemplateRequest
        {
            TargetVideoId = targetVideo.VideoId,
            PromptEnrichment = "Generate better metadata"
        };

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, "/api/videos/ai-template")
        {
            Content = TestHelpers.CreateJsonContent(request)
        };

        requestMessage.Headers.Add("OperationId", TestHelpers.NewOperationId());

        // Act - Spend credits
        var spendResponse = await fixture.HttpClient.SendAsync(requestMessage);

        Assert.Equal(HttpStatusCode.Accepted, spendResponse.StatusCode);

        // Act - Get balance
        var balanceResponse = await fixture.HttpClient.GetAsync("/api/credits/balance");

        // Assert
        Assert.Equal(HttpStatusCode.OK, balanceResponse.StatusCode);
        var result = await TestHelpers.DeserializeAsync<CreditsController.CreditBalanceResponse>(balanceResponse);

        Assert.NotNull(result);
        Assert.Equal(TestConstants.MonthlyCredits - TestConstants.AiTemplateCost, result.Balance);
    }

    [Fact]
    public async Task GetBalance_PeriodDatesArePreserved()
    {
        // Arrange
        await fixture.CleanStateAsync();

        const string userId = MockAuthenticationExtensions.TestSub;
        var periodStart = TestFixture.TestingDateTimeOffset.AddDays(-5);
        var periodEnd = TestFixture.TestingDateTimeOffset.AddDays(25);

        using (var scope = fixture.ApiServices.CreateScope())
        {
            var databaseContext = scope.ServiceProvider.GetRequiredService<TubesterDb>();

            var user = User.Create(
                userId,
                MockAuthenticationExtensions.TestEmail,
                MockAuthenticationExtensions.TestName,
                MockAuthenticationExtensions.TestPicture,
                TestFixture.TestingDateTimeOffset);

            await databaseContext.Users.AddAsync(user, CancellationToken.None);

            var wallet = new Wallet
            {
                UserId = userId,
                Balance = 50,
                PeriodStartUtc = periodStart,
                PeriodEndUtc = periodEnd,
                UpdatedAtUtc = TestFixture.TestingDateTimeOffset
            };

            await databaseContext.Wallets.AddAsync(wallet, CancellationToken.None);

            await databaseContext.SaveChangesAsync(CancellationToken.None);
        }

        // Act
        var response = await fixture.HttpClient.GetAsync("/api/credits/balance");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await TestHelpers.DeserializeAsync<CreditsController.CreditBalanceResponse>(response);

        Assert.NotNull(result);
        Assert.Equal(50, result.Balance);
        Assert.Equal(periodStart, result.PeriodStartUtc);
        Assert.Equal(periodEnd, result.PeriodEndUtc);
    }

    [Fact]
    public async Task GetBalance_MultipleRequests_ReturnsConsistentResult()
    {
        // Arrange
        await fixture.CleanStateAsync();

        const string userId = MockAuthenticationExtensions.TestSub;
        const int expectedBalance = 42;

        using (var scope = fixture.ApiServices.CreateScope())
        {
            var databaseContext = scope.ServiceProvider.GetRequiredService<TubesterDb>();

            var user = User.Create(
                userId,
                MockAuthenticationExtensions.TestEmail,
                MockAuthenticationExtensions.TestName,
                MockAuthenticationExtensions.TestPicture,
                TestFixture.TestingDateTimeOffset);

            await databaseContext.Users.AddAsync(user, CancellationToken.None);

            var wallet = new Wallet
            {
                UserId = userId,
                Balance = expectedBalance,
                PeriodStartUtc = TestFixture.TestingDateTimeOffset,
                PeriodEndUtc = TestFixture.TestingDateTimeOffset.AddMonths(1),
                UpdatedAtUtc = TestFixture.TestingDateTimeOffset
            };

            await databaseContext.Wallets.AddAsync(wallet, CancellationToken.None);

            await databaseContext.SaveChangesAsync(CancellationToken.None);
        }

        // Act
        var response1 = await fixture.HttpClient.GetAsync("/api/credits/balance");
        var response2 = await fixture.HttpClient.GetAsync("/api/credits/balance");
        var response3 = await fixture.HttpClient.GetAsync("/api/credits/balance");

        // Assert
        foreach (var response in new[] { response1, response2, response3 })
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var result = await TestHelpers.DeserializeAsync<CreditsController.CreditBalanceResponse>(response);

            Assert.NotNull(result);
            Assert.Equal(expectedBalance, result.Balance);
        }
    }
}