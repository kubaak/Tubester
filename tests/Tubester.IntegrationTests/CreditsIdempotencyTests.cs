using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;
using Tubester.Abstractions.Analytics;
using Tubester.Abstractions.Credits;
using Tubester.Abstractions.Users;
using Tubester.Application.Contracts.Videos;
using Tubester.Domain;
using Tubester.IntegrationTests.TestHost;
using Tubester.Persistence;
using Tubester.Persistence.Credits;

namespace Tubester.IntegrationTests;

[Collection(nameof(TestCollection))]
public sealed class CreditsIdempotencyTests(TestFixture fixture)
{
    private readonly JsonSerializerOptions _serializerOptions =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    [Fact]
    public async Task CopyTemplate_SameIdempotencyKey_DoesNotDoubleCharge()
    {
        await fixture.ResetDbAsync();

        const string channelId = "credits-idempotency-channel";
        const string uploadsPlaylistId = "ULCreditsIdempotency";
        const string userId = MockAuthenticationExtensions.TestSub;
        const string operationId = "credits-idempotency-operation";

        fixture.ApiFactory.MockCurrentChannelContext
            .Setup(channelContext => channelContext.GetRequiredChannelId())
            .Returns(channelId);

        var sourceVideo = CreateSourceVideo(uploadsPlaylistId);
        var targetVideo = CreateTargetVideo(uploadsPlaylistId);

        using (var serviceScope = fixture.ApiServices.CreateScope())
        {
            var databaseContext = serviceScope.ServiceProvider.GetRequiredService<TubesterDb>();

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
                    "Credits Idempotency Channel",
                    uploadsPlaylistId,
                    TestFixture.TestingDateTimeOffset),
                CancellationToken.None);

            databaseContext.Videos.AddRange(sourceVideo, targetVideo);

            var plan = new Plan
            {
                Code = "CreditsIdempotencyPlan",
                Name = "Credits Idempotency Plan",
                MonthlyCredits = 5,
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

            var copyTemplateCost = new ActionCost
            {
                ActionType = CreditActionType.CopyTemplateExecuted.ToString(),
                Cost = 2,
                IsEnabled = true,
                UpdatedAtUtc = TestFixture.TestingDateTimeOffset,
                Notes = "Credits idempotency test cost for CopyTemplateExecuted."
            };

            await databaseContext.ActionCosts.AddAsync(copyTemplateCost, CancellationToken.None);
            await databaseContext.SaveChangesAsync(CancellationToken.None);
        }

        fixture.ApiFactory.MockYouTubeIntegration
            .Setup(youTubeIntegration => youTubeIntegration.UpdateVideoAsync(
                targetVideo.VideoId,
                sourceVideo.Title!,
                sourceVideo.Description!,
                It.Is<IReadOnlyList<string>>(tags => tags.SequenceEqual(sourceVideo.Tags)),
                targetVideo.CategoryId,
                sourceVideo.DefaultLanguage,
                sourceVideo.DefaultAudioLanguage,
                It.IsAny<(double lat, double lng)?>(),
                targetVideo.LocationDescription,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var request = new CopyVideoTemplateRequest(
            sourceVideo.VideoId,
            targetVideo.VideoId,
            operationId,
            true,
            false,
            true,
            false,
            true
        );

        var requestJson = JsonSerializer.Serialize(request, _serializerOptions);
        var requestContent = new StringContent(requestJson, Encoding.UTF8, "application/json");

        var response1 = await fixture.HttpClient.PostAsync("/api/videos/copy-template", requestContent);
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);

        var response2 = await fixture.HttpClient.PostAsync("/api/videos/copy-template", requestContent);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);

        using (var verificationScope = fixture.ApiServices.CreateScope())
        {
            var databaseContext = verificationScope.ServiceProvider.GetRequiredService<TubesterDb>();

            var wallet = await databaseContext.Wallets
                .AsNoTracking()
                .SingleOrDefaultAsync(entity => entity.UserId == userId);

            Assert.NotNull(wallet);
            Assert.Equal(3, wallet!.Balance); // 5 monthly credits - 2 cost (no double charge)

            var ledgerEntries = await databaseContext.LedgerEntries
                .AsNoTracking()
                .Where(entry => entry.UserId == userId)
                .OrderBy(entry => entry.OccurredAtUtc)
                .ToListAsync();

            // One grant + one spend only
            Assert.Equal(2, ledgerEntries.Count);
        }
    }

    [Fact]
    public async Task CopyTemplate_DifferentIdempotencyKey_DoubleCharge()
    {
        await fixture.ResetDbAsync();

        const string channelId = "credits-idempotency-channel";
        const string uploadsPlaylistId = "ULCreditsIdempotency";
        const string userId = MockAuthenticationExtensions.TestSub;
        const string operationId1 = "credits-idempotency-operation1";
        const string operationId2 = "credits-idempotency-operation2";

        fixture.ApiFactory.MockCurrentChannelContext
            .Setup(channelContext => channelContext.GetRequiredChannelId())
            .Returns(channelId);

        var sourceVideo = CreateSourceVideo(uploadsPlaylistId);
        var targetVideo = CreateTargetVideo(uploadsPlaylistId);

        using (var serviceScope = fixture.ApiServices.CreateScope())
        {
            var databaseContext = serviceScope.ServiceProvider.GetRequiredService<TubesterDb>();

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
                    "Credits Idempotency Channel",
                    uploadsPlaylistId,
                    TestFixture.TestingDateTimeOffset),
                CancellationToken.None);

            databaseContext.Videos.AddRange(sourceVideo, targetVideo);

            var plan = new Plan
            {
                Code = "CreditsIdempotencyPlan",
                Name = "Credits Idempotency Plan",
                MonthlyCredits = 5,
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

            var copyTemplateCost = new ActionCost
            {
                ActionType = CreditActionType.CopyTemplateExecuted.ToString(),
                Cost = 2,
                IsEnabled = true,
                UpdatedAtUtc = TestFixture.TestingDateTimeOffset,
                Notes = "Credits idempotency test cost for CopyTemplateExecuted."
            };

            await databaseContext.ActionCosts.AddAsync(copyTemplateCost, CancellationToken.None);
            await databaseContext.SaveChangesAsync(CancellationToken.None);
        }

        fixture.ApiFactory.MockYouTubeIntegration
            .Setup(youTubeIntegration => youTubeIntegration.UpdateVideoAsync(
                targetVideo.VideoId,
                sourceVideo.Title!,
                sourceVideo.Description!,
                It.Is<IReadOnlyList<string>>(tags => tags.SequenceEqual(sourceVideo.Tags)),
                targetVideo.CategoryId,
                sourceVideo.DefaultLanguage,
                sourceVideo.DefaultAudioLanguage,
                It.IsAny<(double lat, double lng)?>(),
                targetVideo.LocationDescription,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var request1 = new CopyVideoTemplateRequest(
            sourceVideo.VideoId,
            targetVideo.VideoId,
            operationId1,
            true,
            false,
            true,
            false,
            true
        );
        var requestJson1 = JsonSerializer.Serialize(request1, _serializerOptions);
        var requestContent1 = new StringContent(requestJson1, Encoding.UTF8, "application/json");

        var response1 = await fixture.HttpClient.PostAsync("/api/videos/copy-template", requestContent1);
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);

        var request2 = new CopyVideoTemplateRequest(
            sourceVideo.VideoId,
            targetVideo.VideoId,
            operationId2,
            true,
            false,
            true,
            false,
            true
        );

        var requestJson2 = JsonSerializer.Serialize(request2, _serializerOptions);
        var requestContent2 = new StringContent(requestJson2, Encoding.UTF8, "application/json");
        var response2 = await fixture.HttpClient.PostAsync("/api/videos/copy-template", requestContent2);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);

        using (var verificationScope = fixture.ApiServices.CreateScope())
        {
            var databaseContext = verificationScope.ServiceProvider.GetRequiredService<TubesterDb>();

            var wallet = await databaseContext.Wallets
                .AsNoTracking()
                .SingleOrDefaultAsync(entity => entity.UserId == userId);

            Assert.NotNull(wallet);
            Assert.Equal(1, wallet!.Balance); // 5 monthly credits - 2 - 2 cost (double charge)

            var ledgerEntries = await databaseContext.LedgerEntries
                .AsNoTracking()
                .Where(entry => entry.UserId == userId)
                .OrderBy(entry => entry.OccurredAtUtc)
                .ToListAsync();

            // One grant + one spend only
            Assert.Equal(3, ledgerEntries.Count);
        }
    }

    private static Video CreateTargetVideo(string uploadsPlaylistId)
    {
        var video = Video.Create(
            uploadsPlaylistId,
            "credits-idempotency-target-video",
            "Credits Idempotency Target Video",
            "Credits Idempotency Target Description",
            TestFixture.TestingDateTimeOffset.AddDays(-2),
            TimeSpan.FromMinutes(3),
            VideoVisibility.Private,
            ["target"],
            "23",
            "fr",
            "fr",
            null,
            null,
            TestFixture.TestingDateTimeOffset,
            "etag-credits-idempotency-target",
            false
        );

        return video;
    }

    private static Video CreateSourceVideo(string uploadsPlaylistId)
    {
        var video = Video.Create(
            uploadsPlaylistId,
            "credits-idempotency-source-video",
            "Credits Idempotency Source Video",
            "Credits Idempotency Source Description",
            TestFixture.TestingDateTimeOffset.AddDays(-1),
            TimeSpan.FromMinutes(5),
            VideoVisibility.Public,
            ["source", "template"],
            "22",
            "en",
            "en",
            new GeoLocation(37.7749, -122.4194),
            "San Francisco, CA",
            TestFixture.TestingDateTimeOffset,
            "etag-credits-idempotency-source",
            true
        );

        return video;
    }
}
