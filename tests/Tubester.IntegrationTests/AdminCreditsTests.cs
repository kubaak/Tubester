using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tubester.Abstractions.Users;
using Tubester.Api;
using Tubester.IntegrationTests.TestHost;
using Tubester.Persistence;
using Tubester.Persistence.Credits;
using Xunit;

namespace Tubester.IntegrationTests;

[Collection(nameof(TestCollection))]
public sealed class AdminCreditsTests(TestFixture fixture)
{
    private const string Endpoint = "/api/admin/credits/grants";

    [Fact]
    public async Task Grant_ValidRequest_GrantsCreditsToUser()
    {
        // Arrange
        await fixture.CleanStateAsync();

        const string targetUserId = "target-user-id";
        const int grantAmount = 100;

        await CreateUserAsync(
            targetUserId,
            "target@example.com",
            "Target User");

        var request = new AdminCreditsController.CreditGrantRequest
        {
            UserId = targetUserId,
            Amount = grantAmount
        };

        var requestMessage = CreateGrantRequest(
            request,
            operationId: "valid-grant-operation");

        // Act
        var response = await fixture.HttpClient.SendAsync(requestMessage);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await TestHelpers.DeserializeAsync<AdminCreditsController.CreditGrantResponse>(response);

        Assert.True(result.Success);
        Assert.False(result.AlreadyProcessed);
        Assert.Equal(grantAmount, result.NewBalance);
        Assert.Contains("Granted", result.Message);

        using var verificationScope = fixture.ApiServices.CreateScope();
        var dbContext = verificationScope.ServiceProvider.GetRequiredService<TubesterDb>();

        var wallet = await dbContext.Wallets.SingleOrDefaultAsync(w => w.UserId == targetUserId);
        Assert.NotNull(wallet);
        Assert.Equal(grantAmount, wallet.Balance);
        Assert.Equal(TestFixture.TestingDateTimeOffset, wallet.PeriodStartUtc);
        Assert.Equal(TestFixture.TestingDateTimeOffset.AddDays(30), wallet.PeriodEndUtc);

        var ledgerEntries = await dbContext.LedgerEntries
            .Where(e => e.UserId == targetUserId)
            .ToListAsync();

        Assert.Single(ledgerEntries);
        Assert.Equal(grantAmount, ledgerEntries[0].Delta);
    }

    [Fact]
    public async Task Grant_MissingOperationId_ReturnsBadRequest()
    {
        // Arrange
        await fixture.CleanStateAsync();

        var request = new AdminCreditsController.CreditGrantRequest
        {
            UserId = "some-user-id",
            Amount = 10
        };

        var requestMessage = CreateGrantRequest(
            request,
            operationId: null);

        // Act
        var response = await fixture.HttpClient.SendAsync(requestMessage);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var result = await TestHelpers.DeserializeAsync<AdminCreditsController.CreditGrantResponse>(response);

        Assert.False(result.Success);
        Assert.Equal("Missing OperationId header.", result.Message);
    }

    [Fact]
    public async Task Grant_BlankOperationId_ReturnsBadRequest()
    {
        // Arrange
        await fixture.CleanStateAsync();

        var request = new AdminCreditsController.CreditGrantRequest
        {
            UserId = "some-user-id",
            Amount = 10
        };

        var requestMessage = CreateGrantRequest(
            request,
            operationId: "   ");

        // Act
        var response = await fixture.HttpClient.SendAsync(requestMessage);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var result = await TestHelpers.DeserializeAsync<AdminCreditsController.CreditGrantResponse>(response);

        Assert.False(result.Success);
        Assert.Equal("Missing OperationId header.", result.Message);
    }

    [Fact]
    public async Task Grant_OperationIdTooLong_ReturnsBadRequest()
    {
        // Arrange
        await fixture.CleanStateAsync();

        var request = new AdminCreditsController.CreditGrantRequest
        {
            UserId = "some-user-id",
            Amount = 10
        };

        var longOperationId = new string('a', 101);

        var requestMessage = CreateGrantRequest(
            request,
            operationId: longOperationId);

        // Act
        var response = await fixture.HttpClient.SendAsync(requestMessage);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var result = await TestHelpers.DeserializeAsync<AdminCreditsController.CreditGrantResponse>(response);

        Assert.False(result.Success);
        Assert.Contains("too long", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Grant_MissingUserId_ReturnsBadRequest()
    {
        // Arrange
        await fixture.CleanStateAsync();

        var request = new AdminCreditsController.CreditGrantRequest
        {
            UserId = "",
            Amount = 10
        };

        var requestMessage = CreateGrantRequest(
            request,
            operationId: "missing-user-id-operation");

        // Act
        var response = await fixture.HttpClient.SendAsync(requestMessage);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var result = await TestHelpers.DeserializeAsync<AdminCreditsController.CreditGrantResponse>(response);

        Assert.False(result.Success);
        Assert.Contains("UserId", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Grant_ZeroAmount_ReturnsBadRequest()
    {
        // Arrange
        await fixture.CleanStateAsync();

        var request = new AdminCreditsController.CreditGrantRequest
        {
            UserId = "some-user-id",
            Amount = 0
        };

        var requestMessage = CreateGrantRequest(
            request,
            operationId: "zero-amount-operation");

        // Act
        var response = await fixture.HttpClient.SendAsync(requestMessage);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var result = await TestHelpers.DeserializeAsync<AdminCreditsController.CreditGrantResponse>(response);

        Assert.False(result.Success);
        Assert.Contains("Amount", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Grant_NegativeAmount_ReturnsBadRequest()
    {
        // Arrange
        await fixture.CleanStateAsync();

        var request = new AdminCreditsController.CreditGrantRequest
        {
            UserId = "some-user-id",
            Amount = -5
        };

        var requestMessage = CreateGrantRequest(
            request,
            operationId: "negative-amount-operation");

        // Act
        var response = await fixture.HttpClient.SendAsync(requestMessage);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var result = await TestHelpers.DeserializeAsync<AdminCreditsController.CreditGrantResponse>(response);

        Assert.False(result.Success);
        Assert.Contains("Amount", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Grant_SameIdempotencyKey_ReturnsAlreadyProcessed()
    {
        // Arrange
        await fixture.CleanStateAsync();

        const string targetUserId = "idempotency-user-id";
        const int grantAmount = 50;
        const string sameOperationId = "same-operation-idempotency";

        await CreateUserAsync(
            targetUserId,
            "idempotency@example.com",
            "Idempotency User");

        var request = new AdminCreditsController.CreditGrantRequest
        {
            UserId = targetUserId,
            Amount = grantAmount
        };

        var requestMessage1 = CreateGrantRequest(request, sameOperationId);
        var requestMessage2 = CreateGrantRequest(request, sameOperationId);

        // Act
        var response1 = await fixture.HttpClient.SendAsync(requestMessage1);
        var response2 = await fixture.HttpClient.SendAsync(requestMessage2);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);

        var result = await TestHelpers.DeserializeAsync<AdminCreditsController.CreditGrantResponse>(response2);

        Assert.True(result.Success);
        Assert.True(result.AlreadyProcessed);
        Assert.Equal(grantAmount, result.NewBalance);
        Assert.Contains("already processed", result.Message, StringComparison.OrdinalIgnoreCase);

        using var verificationScope = fixture.ApiServices.CreateScope();
        var dbContext = verificationScope.ServiceProvider.GetRequiredService<TubesterDb>();

        var ledgerEntries = await dbContext.LedgerEntries
            .Where(e => e.UserId == targetUserId)
            .ToListAsync();

        Assert.Single(ledgerEntries);

        var wallet = await dbContext.Wallets.SingleAsync(w => w.UserId == targetUserId);
        Assert.Equal(grantAmount, wallet.Balance);
    }

    [Fact]
    public async Task Grant_SameOperationIdWithDifferentAmount_DoesNotGrantAgain()
    {
        // Arrange
        await fixture.CleanStateAsync();

        const string targetUserId = "same-operation-different-amount-user-id";
        const string sameOperationId = "same-operation-different-amount";
        const int firstGrantAmount = 50;
        const int secondGrantAmount = 500;

        await CreateUserAsync(
            targetUserId,
            "same-operation@example.com",
            "Same Operation User");

        var firstRequest = new AdminCreditsController.CreditGrantRequest
        {
            UserId = targetUserId,
            Amount = firstGrantAmount
        };

        var secondRequest = new AdminCreditsController.CreditGrantRequest
        {
            UserId = targetUserId,
            Amount = secondGrantAmount
        };

        var requestMessage1 = CreateGrantRequest(firstRequest, sameOperationId);
        var requestMessage2 = CreateGrantRequest(secondRequest, sameOperationId);

        // Act
        var response1 = await fixture.HttpClient.SendAsync(requestMessage1);
        var response2 = await fixture.HttpClient.SendAsync(requestMessage2);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);

        var result = await TestHelpers.DeserializeAsync<AdminCreditsController.CreditGrantResponse>(response2);

        Assert.True(result.Success);
        Assert.True(result.AlreadyProcessed);
        Assert.Equal(firstGrantAmount, result.NewBalance);

        using var verificationScope = fixture.ApiServices.CreateScope();
        var dbContext = verificationScope.ServiceProvider.GetRequiredService<TubesterDb>();

        var wallet = await dbContext.Wallets.SingleAsync(w => w.UserId == targetUserId);
        Assert.Equal(firstGrantAmount, wallet.Balance);

        var ledgerEntries = await dbContext.LedgerEntries
            .Where(e => e.UserId == targetUserId)
            .ToListAsync();

        Assert.Single(ledgerEntries);
        Assert.Equal(firstGrantAmount, ledgerEntries[0].Delta);
    }

    [Fact]
    public async Task Grant_UserDoesNotExist_ReturnsNotFound()
    {
        // Arrange
        await fixture.CleanStateAsync();

        const string nonExistentUserId = "non-existent-user-id";
        const int grantAmount = 100;

        var request = new AdminCreditsController.CreditGrantRequest
        {
            UserId = nonExistentUserId,
            Amount = grantAmount
        };

        var requestMessage = CreateGrantRequest(
            request,
            operationId: "missing-target-user-operation");

        // Act
        var response = await fixture.HttpClient.SendAsync(requestMessage);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var result = await TestHelpers.DeserializeAsync<AdminCreditsController.CreditGrantResponse>(response);

        Assert.False(result.Success);
        Assert.False(result.AlreadyProcessed);
        Assert.Equal(0, result.NewBalance);
        Assert.Contains("User", result.Message, StringComparison.OrdinalIgnoreCase);

        using var verificationScope = fixture.ApiServices.CreateScope();
        var dbContext = verificationScope.ServiceProvider.GetRequiredService<TubesterDb>();

        // Verify user and wallet were not created
        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.Id == nonExistentUserId);
        Assert.Null(user);

        var wallet = await dbContext.Wallets.FirstOrDefaultAsync(w => w.UserId == nonExistentUserId);
        Assert.Null(wallet);
    }

    [Fact]
    public async Task Grant_ExistingActiveWallet_AddsToBalanceAndKeepsPeriod()
    {
        // Arrange
        await fixture.CleanStateAsync();

        const string targetUserId = "existing-wallet-user-id";
        const int existingBalance = 25;
        const int grantAmount = 75;
        const int expectedNewBalance = existingBalance + grantAmount;

        var periodStartUtc = TestFixture.TestingDateTimeOffset;
        var periodEndUtc = TestFixture.TestingDateTimeOffset.AddMonths(1);

        await CreateUserWithWalletAsync(
            targetUserId,
            "existing@example.com",
            "Existing Wallet User",
            existingBalance,
            periodStartUtc,
            periodEndUtc);

        var request = new AdminCreditsController.CreditGrantRequest
        {
            UserId = targetUserId,
            Amount = grantAmount
        };

        var requestMessage = CreateGrantRequest(
            request,
            operationId: "existing-wallet-operation");

        // Act
        var response = await fixture.HttpClient.SendAsync(requestMessage);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await TestHelpers.DeserializeAsync<AdminCreditsController.CreditGrantResponse>(response);

        Assert.True(result.Success);
        Assert.False(result.AlreadyProcessed);
        Assert.Equal(expectedNewBalance, result.NewBalance);

        using var verificationScope = fixture.ApiServices.CreateScope();
        var dbContext = verificationScope.ServiceProvider.GetRequiredService<TubesterDb>();

        var wallet = await dbContext.Wallets.SingleOrDefaultAsync(w => w.UserId == targetUserId);
        Assert.NotNull(wallet);
        Assert.Equal(expectedNewBalance, wallet.Balance);
        Assert.Equal(periodStartUtc, wallet.PeriodStartUtc);
        Assert.Equal(periodEndUtc, wallet.PeriodEndUtc);

        var ledgerEntries = await dbContext.LedgerEntries
            .Where(e => e.UserId == targetUserId)
            .ToListAsync();

        Assert.Single(ledgerEntries);
        Assert.Equal(grantAmount, ledgerEntries[0].Delta);
    }

    [Fact]
    public async Task Grant_ExpiredWallet_ResetsBalanceAndStartsNewPeriod()
    {
        // Arrange
        await fixture.CleanStateAsync();

        const string targetUserId = "expired-wallet-user-id";
        const int expiredBalance = 25;
        const int grantAmount = 75;

        var expiredPeriodStartUtc = TestFixture.TestingDateTimeOffset.AddDays(-60);
        var expiredPeriodEndUtc = TestFixture.TestingDateTimeOffset.AddDays(-30);

        await CreateUserWithWalletAsync(
            targetUserId,
            "expired@example.com",
            "Expired Wallet User",
            expiredBalance,
            expiredPeriodStartUtc,
            expiredPeriodEndUtc);

        var request = new AdminCreditsController.CreditGrantRequest
        {
            UserId = targetUserId,
            Amount = grantAmount
        };

        var requestMessage = CreateGrantRequest(
            request,
            operationId: "expired-wallet-operation");

        // Act
        var response = await fixture.HttpClient.SendAsync(requestMessage);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await TestHelpers.DeserializeAsync<AdminCreditsController.CreditGrantResponse>(response);

        Assert.True(result.Success);
        Assert.False(result.AlreadyProcessed);
        Assert.Equal(grantAmount, result.NewBalance);

        using var verificationScope = fixture.ApiServices.CreateScope();
        var dbContext = verificationScope.ServiceProvider.GetRequiredService<TubesterDb>();

        var wallet = await dbContext.Wallets.SingleOrDefaultAsync(w => w.UserId == targetUserId);
        Assert.NotNull(wallet);
        Assert.Equal(grantAmount, wallet.Balance);
        Assert.Equal(TestFixture.TestingDateTimeOffset, wallet.PeriodStartUtc);
        Assert.Equal(TestFixture.TestingDateTimeOffset.AddDays(30), wallet.PeriodEndUtc);

        var ledgerEntries = await dbContext.LedgerEntries
            .Where(e => e.UserId == targetUserId)
            .ToListAsync();

        Assert.Single(ledgerEntries);
        Assert.Equal(grantAmount, ledgerEntries[0].Delta);
    }

    private async Task CreateUserAsync(
        string userId,
        string email,
        string name)
    {
        using var scope = fixture.ApiServices.CreateScope();
        var databaseContext = scope.ServiceProvider.GetRequiredService<TubesterDb>();

        var user = User.Create(
            userId,
            email,
            name,
            null,
            TestFixture.TestingDateTimeOffset);

        await databaseContext.Users.AddAsync(user, CancellationToken.None);
        await databaseContext.SaveChangesAsync(CancellationToken.None);
    }

    private async Task CreateUserWithWalletAsync(
        string userId,
        string email,
        string name,
        int balance,
        DateTimeOffset periodStartUtc,
        DateTimeOffset periodEndUtc)
    {
        using var scope = fixture.ApiServices.CreateScope();
        var databaseContext = scope.ServiceProvider.GetRequiredService<TubesterDb>();

        var user = User.Create(
            userId,
            email,
            name,
            null,
            TestFixture.TestingDateTimeOffset);

        await databaseContext.Users.AddAsync(user, CancellationToken.None);

        var wallet = new Wallet
        {
            UserId = userId,
            Balance = balance,
            PeriodStartUtc = periodStartUtc,
            PeriodEndUtc = periodEndUtc,
            UpdatedAtUtc = periodStartUtc
        };

        await databaseContext.Wallets.AddAsync(wallet, CancellationToken.None);
        await databaseContext.SaveChangesAsync(CancellationToken.None);
    }

    private HttpRequestMessage CreateGrantRequest(
        AdminCreditsController.CreditGrantRequest request,
        string? operationId)
    {
        var requestMessage = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = TestHelpers.CreateJsonContent(request)
        };

        if (operationId is not null)
        {
            requestMessage.Headers.TryAddWithoutValidation("OperationId", operationId);
        }

        return requestMessage;
    }
}