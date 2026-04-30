using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Tubester.Abstractions.Credits;
using Tubester.Abstractions.Users;
using Tubester.Api;
using Tubester.Domain;
using Tubester.IntegrationTests.TestHost;
using Tubester.Persistence;
using Tubester.Persistence.Credits;
using Xunit;

namespace Tubester.IntegrationTests;

[Collection(nameof(TestCollection))]
public sealed class CreditsControllerTests(TestFixture fixture)
{
    private readonly JsonSerializerOptions _serializerOptions =
        new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            Converters =
            {
                new JsonStringEnumConverter()
            }
        };

    [Fact]
    public async Task GetBalance_UserHasNoWallet_ReturnsZeroBalance()
    {
        // Arrange
        await fixture.ResetDbAsync();

        // Act
        var response = await fixture.HttpClient.GetAsync("/api/credits/balance");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var responseContent = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<CreditsController.CreditBalanceResponse>(responseContent, _serializerOptions);

        Assert.NotNull(result);
        Assert.Equal(0, result.Balance);
        Assert.Null(result.PeriodStartUtc);
        Assert.Null(result.PeriodEndUtc);
    }

    [Fact]
    public async Task GetBalance_UserHasWallet_ReturnsWalletBalance()
    {
        // Arrange
        await fixture.ResetDbAsync();

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

        var responseContent = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<CreditsController.CreditBalanceResponse>(responseContent, _serializerOptions);

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
        await fixture.ResetDbAsync();

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
        var grantJson = JsonSerializer.Serialize(grantRequest, _serializerOptions);
        var grantContent = new StringContent(grantJson, Encoding.UTF8, "application/json");

        var grantRequestMessage = new HttpRequestMessage(HttpMethod.Post, "/api/admin/credits/grants")
        {
            Content = grantContent
        };
        grantRequestMessage.Headers.Add("OperationId", operationId);

        var grantResponse = await fixture.HttpClient.SendAsync(grantRequestMessage);
        Assert.Equal(HttpStatusCode.OK, grantResponse.StatusCode);

        // Act - Get balance
        var balanceResponse = await fixture.HttpClient.GetAsync("/api/credits/balance");

        // Assert
        Assert.Equal(HttpStatusCode.OK, balanceResponse.StatusCode);

        var responseContent = await balanceResponse.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<CreditsController.CreditBalanceResponse>(responseContent, _serializerOptions);

        Assert.NotNull(result);
        Assert.Equal(expectedBalance, result.Balance);
    }

    [Fact]
    public async Task GetBalance_AfterCreditSpend_ReturnsReducedBalance()
    {
        // Arrange
        await fixture.ResetDbAsync();

        const string userId = MockAuthenticationExtensions.TestSub;
        const string channelId = "balance-spend-channel";
        const string uploadsPlaylistId = "ULBalanceSpend";
        const int initialBalance = 100;
        const int spendCost = 2;

        fixture.ApiFactory.MockCurrentChannelContext
            .Setup(channelContext => channelContext.GetRequiredChannelId())
            .Returns(channelId);

        var targetVideo = Video.Create(
            uploadsPlaylistId,
            "balance-spend-video",
            "Balance Spend Video",
            "Description",
            TestFixture.TestingDateTimeOffset.AddDays(-2),
            TimeSpan.FromMinutes(3),
            VideoVisibility.Private,
            ["test"],
            "23",
            "fr",
            "fr",
            null,
            null,
            TestFixture.TestingDateTimeOffset,
            "etag-balance-spend",
            false
        );

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

            await databaseContext.Channels.AddAsync(Channel.Create(
                    channelId,
                    userId,
                    "Balance Spend Channel",
                    uploadsPlaylistId,
                    TestFixture.TestingDateTimeOffset),
                CancellationToken.None);

            databaseContext.Videos.Add(targetVideo);

            var plan = new Plan
            {
                Code = "BalanceSpendPlan",
                Name = "Balance Spend Plan",
                MonthlyCredits = initialBalance,
                IsActive = true,
                CreatedAtUtc = TestFixture.TestingDateTimeOffset,
                UpdatedAtUtc = TestFixture.TestingDateTimeOffset
            };

            await databaseContext.Plans.AddAsync(plan, CancellationToken.None);
            await databaseContext.SaveChangesAsync(CancellationToken.None);

            var userSubscription = new Subscription
            {
                UserId = userId,
                PlanId = plan.Id,
                PeriodStartUtc = TestFixture.TestingDateTimeOffset,
                PeriodEndUtc = TestFixture.TestingDateTimeOffset.AddMonths(1),
                Status = SubscriptionStatus.Active
            };

            await databaseContext.Subscriptions.AddAsync(userSubscription, CancellationToken.None);

            var aiTemplateEnqueuedCost = new ActionCost
            {
                ActionType = nameof(CreditActionType.AiTemplateEnqueued),
                Cost = spendCost,
                IsEnabled = true,
                UpdatedAtUtc = TestFixture.TestingDateTimeOffset,
                Notes = "Credits controller test cost for AiTemplateEnqueued."
            };

            await databaseContext.ActionCosts.AddAsync(aiTemplateEnqueuedCost, CancellationToken.None);
            await databaseContext.SaveChangesAsync(CancellationToken.None);
        }

        const string operationId = "balance-spend-test";
        var request = new Application.Contracts.Videos.AiVideoTemplateRequest(
            targetVideo.VideoId,
            "Generate better metadata")
        {
            GenerateTitle = true,
            GenerateDescription = true,
            GenerateTags = true
        };

        var requestJson = JsonSerializer.Serialize(request, _serializerOptions);
        var requestContent = new StringContent(requestJson, Encoding.UTF8, "application/json");

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, "/api/videos/ai-template")
        {
            Content = requestContent
        };
        requestMessage.Headers.Add("OperationId", operationId);

        // Act - Spend credits
        var spendResponse = await fixture.HttpClient.SendAsync(requestMessage);
        Assert.Equal(HttpStatusCode.OK, spendResponse.StatusCode);

        // Act - Get balance
        var balanceResponse = await fixture.HttpClient.GetAsync("/api/credits/balance");

        // Assert
        Assert.Equal(HttpStatusCode.OK, balanceResponse.StatusCode);

        var responseContent = await balanceResponse.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<CreditsController.CreditBalanceResponse>(responseContent, _serializerOptions);

        Assert.NotNull(result);
        Assert.Equal(initialBalance - spendCost, result.Balance);
    }

    [Fact]
    public async Task GetBalance_PeriodDatesArePreserved()
    {
        // Arrange
        await fixture.ResetDbAsync();

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

        var responseContent = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<CreditsController.CreditBalanceResponse>(responseContent, _serializerOptions);

        Assert.NotNull(result);
        Assert.Equal(50, result.Balance);
        Assert.Equal(periodStart, result.PeriodStartUtc);
        Assert.Equal(periodEnd, result.PeriodEndUtc);
    }

    [Fact]
    public async Task GetBalance_MultipleRequests_ReturnsConsistentResult()
    {
        // Arrange
        await fixture.ResetDbAsync();

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

        // Act - Make multiple requests
        var response1 = await fixture.HttpClient.GetAsync("/api/credits/balance");
        var response2 = await fixture.HttpClient.GetAsync("/api/credits/balance");
        var response3 = await fixture.HttpClient.GetAsync("/api/credits/balance");

        // Assert - All responses should be consistent
        foreach (var response in new[] { response1, response2, response3 })
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var responseContent = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<CreditsController.CreditBalanceResponse>(responseContent, _serializerOptions);

            Assert.NotNull(result);
            Assert.Equal(expectedBalance, result.Balance);
        }
    }
}
