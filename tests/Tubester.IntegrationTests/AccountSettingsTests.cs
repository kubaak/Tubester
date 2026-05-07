using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tubester.Abstractions.Users;
using Tubester.Application.Account;
using Tubester.IntegrationTests.TestHost;
using Tubester.Persistence;
using Tubester.Persistence.Credits;
using Xunit;

namespace Tubester.IntegrationTests;

[Collection(nameof(TestCollection))]
public sealed class AccountSettingsTests(TestFixture fixture)
{
    private readonly TestHelpers _helpers = new(fixture.ApiServices);

    [Fact]
    public async Task GetSettings_WhenNoSettingsExist_ReturnsDefaultsWithNullSubscription()
    {
        await fixture.CleanStateAsync();

        const string userId = MockAuthenticationExtensions.TestSub;
        using (var scope = fixture.ApiServices.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TubesterDb>();
            db.Users.Add(User.Create(
                userId,
                MockAuthenticationExtensions.TestEmail,
                MockAuthenticationExtensions.TestName,
                MockAuthenticationExtensions.TestPicture,
                TestFixture.TestingDateTimeOffset));
            await db.SaveChangesAsync();
        }

        var response = await fixture.HttpClient.GetAsync("/api/settings/account");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var dto = await response.Content.ReadFromJsonAsync<AccountSettingsDto>();
        Assert.NotNull(dto);
        Assert.Equal(userId, dto.UserId);
        Assert.Equal("system", dto.PreferredTheme);
        Assert.Equal("en", dto.PreferredLanguage);
        Assert.Null(dto.Subscription);
    }

    [Fact]
    public async Task GetSettings_WithActiveSubscription_ReturnsSubscriptionSummary()
    {
        await fixture.CleanStateAsync();

        const string userId = MockAuthenticationExtensions.TestSub;
        using (var scope = fixture.ApiServices.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TubesterDb>();
            db.Users.Add(User.Create(
                userId,
                MockAuthenticationExtensions.TestEmail,
                MockAuthenticationExtensions.TestName,
                MockAuthenticationExtensions.TestPicture,
                TestFixture.TestingDateTimeOffset));

            var plan = new Plan
            {
                Code = "pro",
                Name = "Pro",
                MonthlyCredits = 100,
                IsActive = true,
                CreatedAtUtc = TestFixture.TestingDateTimeOffset,
                UpdatedAtUtc = TestFixture.TestingDateTimeOffset
            };
            db.Plans.Add(plan);
            await db.SaveChangesAsync();

            db.Subscriptions.Add(new Subscription
            {
                UserId = userId,
                PlanId = plan.Id,
                PeriodStartUtc = TestFixture.TestingDateTimeOffset,
                PeriodEndUtc = TestFixture.TestingDateTimeOffset.AddMonths(1),
                Status = SubscriptionStatus.Active
            });
            await db.SaveChangesAsync();
        }

        var response = await fixture.HttpClient.GetAsync("/api/settings/account");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var dto = await response.Content.ReadFromJsonAsync<AccountSettingsDto>();
        Assert.NotNull(dto);
        Assert.NotNull(dto.Subscription);
        Assert.Equal("pro", dto.Subscription.PlanCode);
        Assert.Equal("Pro", dto.Subscription.PlanName);
        Assert.Equal("Active", dto.Subscription.Status);
        Assert.Equal(100, dto.Subscription.MonthlyCredits);
        Assert.Equal(TestFixture.TestingDateTimeOffset, dto.Subscription.PeriodStartUtc);
    }

    [Fact]
    public async Task PutSettings_WithValidRequest_UpdatesPreferences()
    {
        await fixture.CleanStateAsync();

        const string userId = MockAuthenticationExtensions.TestSub;
        using (var scope = fixture.ApiServices.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TubesterDb>();
            db.Users.Add(User.Create(
                userId,
                MockAuthenticationExtensions.TestEmail,
                MockAuthenticationExtensions.TestName,
                MockAuthenticationExtensions.TestPicture,
                TestFixture.TestingDateTimeOffset));
            await db.SaveChangesAsync();
        }

        var request = new UpdateAccountSettingsRequest { PreferredTheme = "dark", PreferredLanguage = "cs" };
        var response = await fixture.HttpClient.PutAsJsonAsync("/api/settings/account", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var dto = await response.Content.ReadFromJsonAsync<AccountSettingsDto>();
        Assert.NotNull(dto);
        Assert.Equal("dark", dto.PreferredTheme);
        Assert.Equal("cs", dto.PreferredLanguage);

        using var verificationScope = fixture.ApiServices.CreateScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<TubesterDb>();
        var settings = await verificationDb.AccountSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == userId);

        Assert.NotNull(settings);
        Assert.Equal("dark", settings.PreferredTheme);
        Assert.Equal("cs", settings.PreferredLanguage);
    }

    [Fact]
    public async Task PutSettings_WithInvalidTheme_ReturnsBadRequest()
    {
        await fixture.CleanStateAsync();

        const string userId = MockAuthenticationExtensions.TestSub;
        using (var scope = fixture.ApiServices.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TubesterDb>();
            db.Users.Add(User.Create(
                userId,
                MockAuthenticationExtensions.TestEmail,
                MockAuthenticationExtensions.TestName,
                MockAuthenticationExtensions.TestPicture,
                TestFixture.TestingDateTimeOffset));
            await db.SaveChangesAsync();
        }

        var request = new UpdateAccountSettingsRequest { PreferredTheme = "invalid-theme", PreferredLanguage = "en" };
        var response = await fixture.HttpClient.PutAsJsonAsync("/api/settings/account", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetSettings_CalledTwice_IsIdempotentAndDoesNotDuplicateSettings()
    {
        await fixture.CleanStateAsync();
        await _helpers.SeedTestDataAsync();

        var firstResponse = await fixture.HttpClient.GetAsync("/api/settings/account");
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

        var secondResponse = await fixture.HttpClient.GetAsync("/api/settings/account");
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);

        using var verificationScope = fixture.ApiServices.CreateScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<TubesterDb>();
        var settingsCount = await verificationDb.AccountSettings
            .AsNoTracking()
            .CountAsync(s => s.UserId == TestConstants.UserId);

        Assert.Equal(1, settingsCount);
    }
}
