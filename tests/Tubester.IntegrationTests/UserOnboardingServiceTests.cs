using Hangfire.States;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tubester.Abstractions.Auth;
using Tubester.Application.Jobs;
using Tubester.IntegrationTests.TestHost;
using Tubester.Persistence;
using Tubester.Persistence.Credits;
using Xunit;

namespace Tubester.IntegrationTests;

[Collection(nameof(TestCollection))]
public sealed class UserOnboardingServiceTests(TestFixture fixture)
{
    private readonly TestHelpers _helpers = new(fixture.ApiServices);
    [Fact]
    public async Task HandleSuccessfulLoginAsync_ForNewUser_CreatesExpectedOnboardingDataAndQueuesInitialScan()
    {
        // Arrange
        await fixture.CleanStateAsync();
        var databaseContext = fixture.ApiServices.GetRequiredService<TubesterDb>();
        var plan = new Plan
        {
            Code = TestConstants.FreePlanCode,
            Name = TestConstants.FreePlanName,
            MonthlyCredits = TestConstants.MonthlyCredits,
            IsActive = true,
            CreatedAtUtc = TestFixture.TestingDateTimeOffset,
            UpdatedAtUtc = TestFixture.TestingDateTimeOffset
        };

        await databaseContext.Plans.AddAsync(plan, CancellationToken.None);
        await databaseContext.SaveChangesAsync(CancellationToken.None);

        using var scope = fixture.ApiServices.CreateScope();
        var onboardingService = scope.ServiceProvider.GetRequiredService<IUserOnboardingService>();

        var context = new SuccessfulLoginContext(
            UserId: MockAuthenticationExtensions.TestSub,
            Email: MockAuthenticationExtensions.TestEmail,
            Name: MockAuthenticationExtensions.TestName,
            Picture: MockAuthenticationExtensions.TestPicture,
            ChannelId: TestConstants.ChannelId,
            LoginAt: TestFixture.TestingDateTimeOffset);

        // Act
        await onboardingService.HandleSuccessfulLoginAsync(context, CancellationToken.None);

        // Assert
        var db = scope.ServiceProvider.GetRequiredService<TubesterDb>();

        var user = await db.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(entity => entity.Id == MockAuthenticationExtensions.TestSub);

        Assert.NotNull(user);
        Assert.Equal(MockAuthenticationExtensions.TestEmail, user.Email);
        Assert.Equal(MockAuthenticationExtensions.TestName, user.Name);
        Assert.Equal(MockAuthenticationExtensions.TestPicture, user.Picture);
        Assert.False(user.IsNew);

        var subscription = await db.Subscriptions
            .AsNoTracking()
            .Include(entity => entity.Plan)
            .SingleOrDefaultAsync(entity => entity.UserId == MockAuthenticationExtensions.TestSub);

        Assert.NotNull(subscription);
        Assert.Equal("free", subscription.Plan.Code);
        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
        Assert.Equal(TestFixture.TestingDateTimeOffset, subscription.PeriodStartUtc);
        Assert.Equal(TestFixture.TestingDateTimeOffset.AddDays(30), subscription.PeriodEndUtc);

        var wallet = await db.Wallets
            .AsNoTracking()
            .SingleOrDefaultAsync(entity => entity.UserId == MockAuthenticationExtensions.TestSub);

        Assert.NotNull(wallet);
        Assert.Equal(TestConstants.MonthlyCredits, wallet.Balance);
        Assert.Equal(TestFixture.TestingDateTimeOffset, wallet.PeriodStartUtc);
        Assert.Equal(TestFixture.TestingDateTimeOffset.AddDays(30), wallet.PeriodEndUtc);

        var ledgerEntry = await db.LedgerEntries
            .AsNoTracking()
            .SingleOrDefaultAsync(entry => entry.UserId == MockAuthenticationExtensions.TestSub);

        Assert.NotNull(ledgerEntry);
        Assert.Equal("PeriodGrant", ledgerEntry.ActionType);
        Assert.Equal(TestConstants.MonthlyCredits, ledgerEntry.Delta);

        var enqueuedJobs = fixture.CapturingJobClient.GetEnqueued<CommentScanJob>();
        var initialScanJob = Assert.Single(enqueuedJobs);

        Assert.IsType<EnqueuedState>(initialScanJob.State);
        Assert.Equal(nameof(CommentScanJob.Run), initialScanJob.Job.Method.Name);
        Assert.Equal(TestConstants.ChannelId, initialScanJob.Job.Args[0]);

        var options = Assert.IsType<CommentScanOptions>(initialScanJob.Job.Args[1]);
        Assert.True(options.InitialRun);
    }

    [Fact]
    public async Task HandleSuccessfulLoginAsync_ForExistingUser_DoesNotQueueInitialScanAgain()
    {
        // Arrange
        await fixture.CleanStateAsync();
        _ = await _helpers.SeedTestDataAsync(new TestDataOptions { CreateSubscription = false });

        using var scope = fixture.ApiServices.CreateScope();
        var onboardingService = scope.ServiceProvider.GetRequiredService<IUserOnboardingService>();

        var context = new SuccessfulLoginContext(
            UserId: MockAuthenticationExtensions.TestSub,
            Email: MockAuthenticationExtensions.TestEmail,
            Name: MockAuthenticationExtensions.TestName,
            Picture: MockAuthenticationExtensions.TestPicture,
            ChannelId: TestConstants.ChannelId,
            LoginAt: TestFixture.TestingDateTimeOffset);

        await onboardingService.HandleSuccessfulLoginAsync(context, CancellationToken.None);
        fixture.CapturingJobClient.Clear();

        // Act
        await onboardingService.HandleSuccessfulLoginAsync(context, CancellationToken.None);

        // Assert
        var db = scope.ServiceProvider.GetRequiredService<TubesterDb>();

        var subscriptions = await db.Subscriptions
            .AsNoTracking()
            .Where(entity => entity.UserId == MockAuthenticationExtensions.TestSub)
            .ToListAsync();

        Assert.Single(subscriptions);

        var wallets = await db.Wallets
            .AsNoTracking()
            .Where(entity => entity.UserId == MockAuthenticationExtensions.TestSub)
            .ToListAsync();

        Assert.Single(wallets);

        var channelSettings = await db.ChannelSettings
            .AsNoTracking()
            .Where(entity => entity.ChannelId == TestConstants.ChannelId)
            .ToListAsync();

        Assert.Single(channelSettings);

        var enqueuedJobs = fixture.CapturingJobClient.GetEnqueued<CommentScanJob>();
        Assert.Empty(enqueuedJobs);
    }

    [Fact]
    public async Task HandleSuccessfulLoginAsync_WhenChannelIdIsNull_DoesNotCreateChannelSettingsOrQueueInitialScan()
    {
        // Arrange
        await fixture.CleanStateAsync();
        fixture.CapturingJobClient.Clear();

        await EnsureFreePlanAsync();

        using var scope = fixture.ApiServices.CreateScope();
        var onboardingService = scope.ServiceProvider.GetRequiredService<IUserOnboardingService>();

        var context = new SuccessfulLoginContext(
            UserId: MockAuthenticationExtensions.TestSub,
            Email: MockAuthenticationExtensions.TestEmail,
            Name: MockAuthenticationExtensions.TestName,
            Picture: MockAuthenticationExtensions.TestPicture,
            ChannelId: null,
            LoginAt: TestFixture.TestingDateTimeOffset);

        // Act
        await onboardingService.HandleSuccessfulLoginAsync(context, CancellationToken.None);

        // Assert
        await using var assertScope = fixture.ApiServices.CreateAsyncScope();
        var db = assertScope.ServiceProvider.GetRequiredService<TubesterDb>();

        var user = await db.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(entity => entity.Id == MockAuthenticationExtensions.TestSub);

        Assert.NotNull(user);
        Assert.Equal(MockAuthenticationExtensions.TestEmail, user.Email);
        Assert.Equal(MockAuthenticationExtensions.TestName, user.Name);
        Assert.Equal(MockAuthenticationExtensions.TestPicture, user.Picture);

        // Important: with the safer onboarding logic, the user should remain new
        // because channel onboarding and initial scan could not happen yet.
        Assert.True(user.IsNew);

        var subscription = await db.Subscriptions
            .AsNoTracking()
            .Include(entity => entity.Plan)
            .SingleOrDefaultAsync(entity => entity.UserId == MockAuthenticationExtensions.TestSub);

        Assert.NotNull(subscription);
        Assert.Equal(TestConstants.FreePlanCode, subscription.Plan.Code);
        Assert.Equal(SubscriptionStatus.Active, subscription.Status);

        var wallet = await db.Wallets
            .AsNoTracking()
            .SingleOrDefaultAsync(entity => entity.UserId == MockAuthenticationExtensions.TestSub);

        Assert.NotNull(wallet);
        Assert.Equal(TestConstants.MonthlyCredits, wallet.Balance);

        var channelSettings = await db.ChannelSettings
            .AsNoTracking()
            .Where(entity => entity.ChannelId == TestConstants.ChannelId)
            .ToListAsync();

        Assert.Empty(channelSettings);

        var channels = await db.Channels
            .AsNoTracking()
            .ToListAsync();

        Assert.Empty(channels);

        var enqueuedJobs = fixture.CapturingJobClient.GetEnqueued<CommentScanJob>();
        Assert.Empty(enqueuedJobs);
    }

    private async Task EnsureFreePlanAsync()
    {
        await using var scope = fixture.ApiServices.CreateAsyncScope();
        var databaseContext = scope.ServiceProvider.GetRequiredService<TubesterDb>();

        var planExists = await databaseContext.Plans
            .AnyAsync(entity => entity.Code == TestConstants.FreePlanCode);

        if (planExists)
        {
            return;
        }

        var plan = new Plan
        {
            Code = TestConstants.FreePlanCode,
            Name = TestConstants.FreePlanName,
            MonthlyCredits = TestConstants.MonthlyCredits,
            IsActive = true,
            CreatedAtUtc = TestFixture.TestingDateTimeOffset,
            UpdatedAtUtc = TestFixture.TestingDateTimeOffset
        };

        await databaseContext.Plans.AddAsync(plan, CancellationToken.None);
        await databaseContext.SaveChangesAsync(CancellationToken.None);
    }
}