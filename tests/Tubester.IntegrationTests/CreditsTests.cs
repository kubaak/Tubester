using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Tubester.Abstractions.Credits;
using Tubester.Abstractions.Users;
using Tubester.Application.Contracts.Replies;
using Tubester.Application.Contracts.Videos;
using Tubester.Application.Jobs;
using Tubester.Domain;
using Tubester.IntegrationTests.TestHost;
using Tubester.Persistence;
using Tubester.Persistence.Credits;
using Xunit;

namespace Tubester.IntegrationTests;

[Collection(nameof(TestCollection))]
public sealed class CreditsTests(TestFixture fixture)
{
    private readonly JsonSerializerOptions _serializerOptions =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private const string OperationId = "credits-idempotency-operation";

    [Fact]
    public async Task AiTemplateEnqueue_WithSufficientCredits_UpdatesWalletAndLedger()
    {
        await fixture.ResetDbAsync();

        const string channelId = "credits-ai-template-sufficient-channel";
        const string uploadsPlaylistId = "ULCreditsAiTemplateSufficient";
        const string userId = MockAuthenticationExtensions.TestSub;

        fixture.ApiFactory.MockCurrentChannelContext
            .Setup(channelContext => channelContext.GetRequiredChannelId())
            .Returns(channelId);

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
                    "Credits AI Template Sufficient Channel",
                    uploadsPlaylistId,
                    TestFixture.TestingDateTimeOffset),
                CancellationToken.None);

            databaseContext.Videos.Add(targetVideo);

            var plan = new Plan
            {
                Code = "CreditsAiTemplateSufficientPlan",
                Name = "Credits AI Template Sufficient Plan",
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

            var aiTemplateEnqueuedCost = new ActionCost
            {
                ActionType = CreditActionType.AiTemplateEnqueued.ToString(),
                Cost = 2,
                IsEnabled = true,
                UpdatedAtUtc = TestFixture.TestingDateTimeOffset,
                Notes = "Credits integration test cost for AiTemplateEnqueued."
            };

            await databaseContext.ActionCosts.AddAsync(aiTemplateEnqueuedCost, CancellationToken.None);
            await databaseContext.SaveChangesAsync(CancellationToken.None);
        }

        var request = new AiVideoTemplateRequest(
            targetVideo.VideoId,
            "Generate better metadata for credits test")
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
        requestMessage.Headers.Add("OperationId", OperationId);

        var response = await fixture.HttpClient.SendAsync(requestMessage);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using (var verificationScope = fixture.ApiServices.CreateScope())
        {
            var databaseContext = verificationScope.ServiceProvider.GetRequiredService<TubesterDb>();

            var wallet = await databaseContext.Wallets
                .AsNoTracking()
                .SingleOrDefaultAsync(entity => entity.UserId == userId);

            Assert.NotNull(wallet);
            Assert.Equal(3, wallet!.Balance); // 5 monthly credits - 2 cost

            var ledgerEntries = await databaseContext.LedgerEntries
                .AsNoTracking()
                .Where(entry => entry.UserId == userId)
                .OrderBy(entry => entry.OccurredAtUtc)
                .ToListAsync();

            Assert.Equal(2, ledgerEntries.Count);

            var grantEntry = Assert.Single(ledgerEntries, entry => entry.Delta > 0);
            Assert.Equal("PeriodGrant", grantEntry.ActionType);

            var spendEntry = Assert.Single(ledgerEntries, entry => entry.Delta < 0);
            Assert.Equal(CreditActionType.AiTemplateEnqueued.ToString(), spendEntry.ActionType);
            Assert.Equal(-2, spendEntry.Delta);
            Assert.Equal(targetVideo.VideoId, spendEntry.ReferenceId);
        }
    }

    [Fact]
    public async Task AiTemplateEnqueue_WithInsufficientCredits_ReturnsForbidden_AndDoesNotPersistWalletOrLedger()
    {
        await fixture.ResetDbAsync();

        const string channelId = "credits-ai-template-forbidden-channel";
        const string uploadsPlaylistId = "ULCreditsAiTemplateForbidden";
        const string userId = MockAuthenticationExtensions.TestSub;

        fixture.ApiFactory.MockCurrentChannelContext
            .Setup(channelContext => channelContext.GetRequiredChannelId())
            .Returns(channelId);

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
                    "Credits AI Template Forbidden Channel",
                    uploadsPlaylistId,
                    TestFixture.TestingDateTimeOffset),
                CancellationToken.None);

            databaseContext.Videos.Add(targetVideo);

            var plan = new Plan
            {
                Code = "CreditsAiTemplateForbiddenPlan",
                Name = "Credits AI Template Forbidden Plan",
                MonthlyCredits = 1,
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
                ActionType = CreditActionType.AiTemplateEnqueued.ToString(),
                Cost = 2,
                IsEnabled = true,
                UpdatedAtUtc = TestFixture.TestingDateTimeOffset,
                Notes = "Credits integration test forbidden cost for AiTemplateEnqueued."
            };

            await databaseContext.ActionCosts.AddAsync(aiTemplateEnqueuedCost, CancellationToken.None);
            await databaseContext.SaveChangesAsync(CancellationToken.None);
        }

        var request = new AiVideoTemplateRequest(
            targetVideo.VideoId,
            "Generate better metadata for credits test")
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
        requestMessage.Headers.Add("OperationId", OperationId);

        var response = await fixture.HttpClient.SendAsync(requestMessage);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var responseBody = await response.Content.ReadAsStringAsync();
        Assert.Contains("Insufficient credits to enqueue AI templating.", responseBody);

        using (var verificationScope = fixture.ApiServices.CreateScope())
        {
            var databaseContext = verificationScope.ServiceProvider.GetRequiredService<TubesterDb>();

            var wallet = await databaseContext.Wallets
                .AsNoTracking()
                .SingleOrDefaultAsync(entity => entity.UserId == userId);
            Assert.NotNull(wallet);
            Assert.Equal(1, wallet.Balance); //only granted monthly credit

            var ledgerEntries = await databaseContext.LedgerEntries
                .AsNoTracking()
                .Where(entry => entry.UserId == userId)
                .ToListAsync();
            Assert.NotNull(ledgerEntries);
            Assert.Single(ledgerEntries);
            Assert.Equal(1, ledgerEntries[0].Delta);
        }
    }

    [Fact]
    public async Task AiTemplateEnqueue_WithSufficientCredits_DeductsCreditsAndAppendsLedgerEntry()
    {
        await fixture.ResetDbAsync();

        const string channelId = "credits-ai-template-channel";
        const string uploadsPlaylistId = "ULCreditsAiTemplate";
        const string userId = MockAuthenticationExtensions.TestSub;

        fixture.ApiFactory.MockCurrentChannelContext
            .Setup(channelContext => channelContext.GetRequiredChannelId())
            .Returns(channelId);

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
                    "Credits AI Template Channel",
                    uploadsPlaylistId,
                    TestFixture.TestingDateTimeOffset),
                CancellationToken.None);

            databaseContext.Videos.Add(targetVideo);

            var plan = new Plan
            {
                Code = "CreditsAiTemplatePlan",
                Name = "Credits AI Template Plan",
                MonthlyCredits = 4,
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
                ActionType = CreditActionType.AiTemplateEnqueued.ToString(),
                Cost = 1,
                IsEnabled = true,
                UpdatedAtUtc = TestFixture.TestingDateTimeOffset,
                Notes = "Credits integration test cost for AiTemplateEnqueued."
            };

            await databaseContext.ActionCosts.AddAsync(aiTemplateEnqueuedCost, CancellationToken.None);
            await databaseContext.SaveChangesAsync(CancellationToken.None);
        }

        var request = new AiVideoTemplateRequest(
            targetVideo.VideoId,
            "Generate better metadata for credits test")
        {
            GenerateTitle = true,
            GenerateDescription = true,
            GenerateTags = true
        };

        var serializedRequest = JsonSerializer.Serialize(request, _serializerOptions);
        var requestContent = new StringContent(serializedRequest, Encoding.UTF8, "application/json");

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, "/api/videos/ai-template")
        {
            Content = requestContent
        };
        requestMessage.Headers.Add("OperationId", OperationId);

        var response = await fixture.HttpClient.SendAsync(requestMessage);
        
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using (var verificationScope = fixture.ApiServices.CreateScope())
        {
            var databaseContext = verificationScope.ServiceProvider.GetRequiredService<TubesterDb>();

            var wallet = await databaseContext.Wallets
                .AsNoTracking()
                .SingleOrDefaultAsync(entity => entity.UserId == userId);

            Assert.NotNull(wallet);
            Assert.Equal(3, wallet!.Balance); // 4 monthly credits - 1 cost

            var ledgerEntries = await databaseContext.LedgerEntries
                .AsNoTracking()
                .Where(entry => entry.UserId == userId)
                .OrderBy(entry => entry.OccurredAtUtc)
                .ToListAsync();

            Assert.Equal(2, ledgerEntries.Count);
            Assert.Contains(ledgerEntries, entry => entry.ActionType == "PeriodGrant" && entry.Delta == 4);

            var spendEntry = Assert.Single(ledgerEntries, entry => entry.Delta < 0);
            Assert.Equal(CreditActionType.AiTemplateEnqueued.ToString(), spendEntry.ActionType);
            Assert.Equal(-1, spendEntry.Delta);
            Assert.Equal(targetVideo.VideoId, spendEntry.ReferenceId);
        }
    }

    private static Video CreateTargetVideo(string uploadsPlaylistId)
    {
        var video = Video.Create(
            uploadsPlaylistId,
            "credits-target-video",
            "Credits Target Video",
            "Credits Target Description",
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
            "etag-credits-target",
            false
        );

        return video;
    }

    [Fact]
    public async Task AiTemplateSubmitted_WithSufficientCredits_DeductsCreditsAndAppendsLedgerEntry()
    {
        await fixture.ResetDbAsync();

        const string channelId = "credits-ai-template-submitted-channel";
        const string uploadsPlaylistId = "ULCreditsAiTemplateSubmitted";
        const string userId = MockAuthenticationExtensions.TestSub;

        fixture.ApiFactory.MockCurrentChannelContext
            .Setup(channelContext => channelContext.GetRequiredChannelId())
            .Returns(channelId);

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
                    "Credits AI Template Submitted Channel",
                    uploadsPlaylistId,
                    TestFixture.TestingDateTimeOffset),
                CancellationToken.None);

            databaseContext.Videos.Add(targetVideo);

            var plan = new Plan
            {
                Code = "CreditsAiTemplateSubmittedPlan",
                Name = "Credits AI Template Submitted Plan",
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

            var aiTemplateSubmittedCost = new ActionCost
            {
                ActionType = CreditActionType.AiTemplateSubmitted.ToString(),
                Cost = 2,
                IsEnabled = true,
                UpdatedAtUtc = TestFixture.TestingDateTimeOffset,
                Notes = "Credits integration test cost for AiTemplateSubmitted."
            };

            await databaseContext.ActionCosts.AddAsync(aiTemplateSubmittedCost, CancellationToken.None);
            await databaseContext.SaveChangesAsync(CancellationToken.None);
        }

        const string newTitle = "Updated Title";
        const string newDescription = "Updated Description";
        var newTags = new[] { "new-tag" };

        fixture.ApiFactory.MockYouTubeIntegration
            .Setup(youTubeIntegration => youTubeIntegration.UpdateVideoAsync(
                targetVideo.VideoId,
                newTitle,
                newDescription,
                It.Is<IReadOnlyList<string>>(tags => tags.SequenceEqual(newTags)),
                targetVideo.CategoryId,
                targetVideo.DefaultLanguage,
                targetVideo.DefaultAudioLanguage,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var request = new UpdateVideoMetadataRequest(
            targetVideo.VideoId,
            newTitle,
            newDescription,
            newTags);

        var serializedRequest = JsonSerializer.Serialize(request, _serializerOptions);
        var requestContent = new StringContent(serializedRequest, Encoding.UTF8, "application/json");

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, "/api/videos/update")
        {
            Content = requestContent
        };
        requestMessage.Headers.Add("OperationId", OperationId);

        var response = await fixture.HttpClient.SendAsync(requestMessage);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using (var verificationScope = fixture.ApiServices.CreateScope())
        {
            var databaseContext = verificationScope.ServiceProvider.GetRequiredService<TubesterDb>();

            var wallet = await databaseContext.Wallets
                .AsNoTracking()
                .SingleOrDefaultAsync(entity => entity.UserId == userId);

            Assert.NotNull(wallet);
            Assert.Equal(3, wallet!.Balance); // 5 monthly credits - 2 cost

            var ledgerEntries = await databaseContext.LedgerEntries
                .AsNoTracking()
                .Where(entry => entry.UserId == userId)
                .OrderBy(entry => entry.OccurredAtUtc)
                .ToListAsync();

            Assert.Equal(2, ledgerEntries.Count);
            Assert.Contains(ledgerEntries, entry => entry.ActionType == "PeriodGrant" && entry.Delta == 5);

            var spendEntry = Assert.Single(ledgerEntries, entry => entry.Delta < 0);
            Assert.Equal(CreditActionType.AiTemplateSubmitted.ToString(), spendEntry.ActionType);
            Assert.Equal(-2, spendEntry.Delta);
            Assert.Equal(targetVideo.VideoId, spendEntry.ReferenceId);
        }
    }

    [Fact]
    public async Task AiTemplateSubmitted_WithInsufficientCredits_ReturnsForbidden_AndDoesNotDeductCredits()
    {
        await fixture.ResetDbAsync();

        const string channelId = "credits-ai-template-submitted-forbidden-channel";
        const string uploadsPlaylistId = "ULCreditsAiTemplateSubmittedForbidden";
        const string userId = MockAuthenticationExtensions.TestSub;

        fixture.ApiFactory.MockCurrentChannelContext
            .Setup(channelContext => channelContext.GetRequiredChannelId())
            .Returns(channelId);

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
                    "Credits AI Template Submitted Forbidden Channel",
                    uploadsPlaylistId,
                    TestFixture.TestingDateTimeOffset),
                CancellationToken.None);

            databaseContext.Videos.Add(targetVideo);

            var plan = new Plan
            {
                Code = "CreditsAiTemplateSubmittedForbiddenPlan",
                Name = "Credits AI Template Submitted Forbidden Plan",
                MonthlyCredits = 1,
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

            var aiTemplateSubmittedCost = new ActionCost
            {
                ActionType = CreditActionType.AiTemplateSubmitted.ToString(),
                Cost = 5,
                IsEnabled = true,
                UpdatedAtUtc = TestFixture.TestingDateTimeOffset,
                Notes = "Credits integration test cost for AiTemplateSubmitted insufficient credits."
            };

            await databaseContext.ActionCosts.AddAsync(aiTemplateSubmittedCost, CancellationToken.None);
            await databaseContext.SaveChangesAsync(CancellationToken.None);
        }

        var request = new UpdateVideoMetadataRequest(
            targetVideo.VideoId,
            "Updated Title",
            "Updated Description",
            ["new-tag"]);

        var serializedRequest = JsonSerializer.Serialize(request, _serializerOptions);
        var requestContent = new StringContent(serializedRequest, Encoding.UTF8, "application/json");

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, "/api/videos/update")
        {
            Content = requestContent
        };
        requestMessage.Headers.Add("OperationId", OperationId);

        var response = await fixture.HttpClient.SendAsync(requestMessage);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var responseBody = await response.Content.ReadAsStringAsync();
        Assert.Contains("Insufficient credits to submit AI template changes.", responseBody);

        using (var verificationScope = fixture.ApiServices.CreateScope())
        {
            var databaseContext = verificationScope.ServiceProvider.GetRequiredService<TubesterDb>();

            var wallet = await databaseContext.Wallets
                .AsNoTracking()
                .SingleOrDefaultAsync(entity => entity.UserId == userId);
            Assert.NotNull(wallet);
            Assert.Equal(1, wallet.Balance); // only granted monthly credit

            var ledgerEntries = await databaseContext.LedgerEntries
                .AsNoTracking()
                .Where(entry => entry.UserId == userId)
                .ToListAsync();
            Assert.NotNull(ledgerEntries);
            Assert.Single(ledgerEntries);
            Assert.Equal(1, ledgerEntries[0].Delta);
        }
    }

    [Fact]
    public async Task ReplyPostedToYouTube_WithSufficientCredits_DeductsCreditsAndAppendsLedgerEntry()
    {
        await fixture.ResetDbAsync();
        fixture.ApiFactory.MockYouTubeIntegration.Reset();

        const string userId = MockAuthenticationExtensions.TestSub;

        var reply = Reply.Create(
            "credits-comment-1",
            "credits-video-1",
            "Test Video",
            "Test comment",
            TestFixture.TestingDateTimeOffset);
        reply.SuggestText("Suggested text", TestFixture.TestingDateTimeOffset.AddMinutes(5));

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

            var plan = new Plan
            {
                Code = "CreditsReplyPostedPlan",
                Name = "Credits Reply Posted Plan",
                MonthlyCredits = 3,
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

            databaseContext.Replies.Add(reply);

            var replyPostedCost = new ActionCost
            {
                ActionType = CreditActionType.ReplyPostedToYouTube.ToString(),
                Cost = 1,
                IsEnabled = true,
                UpdatedAtUtc = TestFixture.TestingDateTimeOffset,
                Notes = "Credits integration test cost for ReplyPostedToYouTube."
            };

            await databaseContext.ActionCosts.AddAsync(replyPostedCost, CancellationToken.None);
            await databaseContext.SaveChangesAsync(CancellationToken.None);
        }

        const string approvedText = "Approved reply text";

        fixture.ApiFactory.MockYouTubeIntegration
            .Setup(youTubeIntegration => youTubeIntegration.ReplyAsync(
                reply.CommentId,
                approvedText,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var batchRequest = new BatchDecisionRequest([new DraftDecisionDto(reply.CommentId, approvedText)]);
        var serializedRequest = JsonSerializer.Serialize(batchRequest, _serializerOptions);
        var requestContent = new StringContent(serializedRequest, Encoding.UTF8, "application/json");

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, "/api/replies/approve")
        {
            Content = requestContent
        };
        requestMessage.Headers.Add("OperationId", OperationId);

        var response = await fixture.HttpClient.SendAsync(requestMessage);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using (var verificationScope = fixture.ApiServices.CreateScope())
        {
            var databaseContext = verificationScope.ServiceProvider.GetRequiredService<TubesterDb>();

            var wallet = await databaseContext.Wallets
                .AsNoTracking()
                .SingleOrDefaultAsync(entity => entity.UserId == userId);

            Assert.NotNull(wallet);
            Assert.Equal(2, wallet!.Balance); // 3 monthly credits - 1 cost

            var ledgerEntries = await databaseContext.LedgerEntries
                .AsNoTracking()
                .Where(entry => entry.UserId == userId)
                .OrderBy(entry => entry.OccurredAtUtc)
                .ToListAsync();

            Assert.Equal(2, ledgerEntries.Count);
            Assert.Contains(ledgerEntries, entry => entry.ActionType == "PeriodGrant" && entry.Delta == 3);

            var spendEntry = Assert.Single(ledgerEntries, entry => entry.Delta < 0);
            Assert.Equal(CreditActionType.ReplyPostedToYouTube.ToString(), spendEntry.ActionType);
            Assert.Equal(-1, spendEntry.Delta);
            Assert.Equal(reply.CommentId, spendEntry.ReferenceId);
        }
    }

    [Fact]
    public async Task ReplyPostedToYouTube_WithInsufficientCredits_FailsWithInsufficientCreditsMessage()
    {
        await fixture.ResetDbAsync();
        fixture.ApiFactory.MockYouTubeIntegration.Reset();

        const string userId = MockAuthenticationExtensions.TestSub;

        var reply = Reply.Create(
            "credits-insufficient-comment-1",
            "credits-insufficient-video-1",
            "Test Video",
            "Test comment",
            TestFixture.TestingDateTimeOffset);
        reply.SuggestText("Suggested text", TestFixture.TestingDateTimeOffset.AddMinutes(5));

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

            var plan = new Plan
            {
                Code = "CreditsReplyPostedInsufficientPlan",
                Name = "Credits Reply Posted Insufficient Plan",
                MonthlyCredits = 1,
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

            databaseContext.Replies.Add(reply);

            var replyPostedCost = new ActionCost
            {
                ActionType = CreditActionType.ReplyPostedToYouTube.ToString(),
                Cost = 5,
                IsEnabled = true,
                UpdatedAtUtc = TestFixture.TestingDateTimeOffset,
                Notes = "Credits integration test cost for ReplyPostedToYouTube insufficient credits."
            };

            await databaseContext.ActionCosts.AddAsync(replyPostedCost, CancellationToken.None);
            await databaseContext.SaveChangesAsync(CancellationToken.None);
        }

        var batchRequest = new BatchDecisionRequest([new DraftDecisionDto(reply.CommentId, "Approved text")]);
        var serializedRequest = JsonSerializer.Serialize(batchRequest, _serializerOptions);
        var requestContent = new StringContent(serializedRequest, Encoding.UTF8, "application/json");

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, "/api/replies/approve")
        {
            Content = requestContent
        };
        requestMessage.Headers.Add("OperationId", OperationId);

        var response = await fixture.HttpClient.SendAsync(requestMessage);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var responseContent = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<BatchDecisionResultDto>(responseContent, _serializerOptions);

        Assert.NotNull(result);
        Assert.Equal(1, result!.Total);
        Assert.Equal(0, result.Succeeded);
        Assert.Equal(1, result.Failed);
        Assert.Contains(result.Items, item => item.CommentId == reply.CommentId && !item.Success && item.Error == "Insufficient credits.");

        using (var verificationScope = fixture.ApiServices.CreateScope())
        {
            var databaseContext = verificationScope.ServiceProvider.GetRequiredService<TubesterDb>();

            var wallet = await databaseContext.Wallets
                .AsNoTracking()
                .SingleOrDefaultAsync(entity => entity.UserId == userId);
            Assert.NotNull(wallet);
            Assert.Equal(1, wallet.Balance); // only granted monthly credit

            var ledgerEntries = await databaseContext.LedgerEntries
                .AsNoTracking()
                .Where(entry => entry.UserId == userId)
                .ToListAsync();
            Assert.NotNull(ledgerEntries);
            Assert.Single(ledgerEntries);
            Assert.Equal(1, ledgerEntries[0].Delta);
        }
    }

    [Fact]
    public async Task TrySpend_WhenUserResubscribesWithNewPeriod_GrantsFreshCredits()
    {
        await fixture.ResetDbAsync();

        const string channelId = "credits-resubscribe-channel";
        const string uploadsPlaylistId = "ULCreditsResubscribe";
        const string userId = MockAuthenticationExtensions.TestSub;

        fixture.ApiFactory.MockCurrentChannelContext
            .Setup(channelContext => channelContext.GetRequiredChannelId())
            .Returns(channelId);

        var targetVideo = CreateTargetVideo(uploadsPlaylistId);

        // Old subscription period: Jan 15 - Feb 15 (still valid on Feb 10)
        var oldPeriodStart = TestFixture.TestingDateTimeOffset.AddDays(-10);
        var oldPeriodEnd = TestFixture.TestingDateTimeOffset.AddDays(20);

        // New subscription period: Feb 5 - Mar 5 (user resubscribed)
        var newPeriodStart = TestFixture.TestingDateTimeOffset.AddDays(-5);
        var newPeriodEnd = TestFixture.TestingDateTimeOffset.AddDays(25);

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
                    "Credits Resubscribe Channel",
                    uploadsPlaylistId,
                    TestFixture.TestingDateTimeOffset),
                CancellationToken.None);

            databaseContext.Videos.Add(targetVideo);

            var plan = new Plan
            {
                Code = "CreditsResubscribePlan",
                Name = "Credits Resubscribe Plan",
                MonthlyCredits = 10,
                IsActive = true,
                CreatedAtUtc = TestFixture.TestingDateTimeOffset,
                UpdatedAtUtc = TestFixture.TestingDateTimeOffset
            };

            await databaseContext.Plans.AddAsync(plan, CancellationToken.None);
            await databaseContext.SaveChangesAsync(CancellationToken.None);

            // Create a wallet from the OLD subscription period with only 2 credits remaining
            var oldWallet = new Wallet
            {
                UserId = userId,
                Balance = 2,
                PeriodStartUtc = oldPeriodStart,
                PeriodEndUtc = oldPeriodEnd,
                UpdatedAtUtc = TestFixture.TestingDateTimeOffset.AddDays(-5)
            };

            await databaseContext.Wallets.AddAsync(oldWallet, CancellationToken.None);

            // Create a NEW subscription with different period dates (user resubscribed)
            var newSubscription = new Subscription
            {
                UserId = userId,
                PlanId = plan.Id,
                PeriodStartUtc = newPeriodStart,
                PeriodEndUtc = newPeriodEnd,
                Status = SubscriptionStatus.Active
            };

            await databaseContext.Subscriptions.AddAsync(newSubscription, CancellationToken.None);

            var aiTemplateEnqueuedCost = new ActionCost
            {
                ActionType = CreditActionType.AiTemplateEnqueued.ToString(),
                Cost = 5,
                IsEnabled = true,
                UpdatedAtUtc = TestFixture.TestingDateTimeOffset,
                Notes = "Credits integration test cost for resubscription scenario."
            };

            await databaseContext.ActionCosts.AddAsync(aiTemplateEnqueuedCost, CancellationToken.None);
            await databaseContext.SaveChangesAsync(CancellationToken.None);
        }

        var request = new AiVideoTemplateRequest(
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
        requestMessage.Headers.Add("OperationId", OperationId);

        var response = await fixture.HttpClient.SendAsync(requestMessage);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using (var verificationScope = fixture.ApiServices.CreateScope())
        {
            var databaseContext = verificationScope.ServiceProvider.GetRequiredService<TubesterDb>();

            var wallet = await databaseContext.Wallets
                .AsNoTracking()
                .SingleOrDefaultAsync(entity => entity.UserId == userId);

            Assert.NotNull(wallet);
            // Should have fresh 10 credits from new subscription minus 5 cost = 5
            // NOT 2 credits (old wallet) minus 5 which would fail
            Assert.Equal(5, wallet!.Balance);

            // Wallet period should now match the new subscription period
            Assert.Equal(newPeriodStart, wallet.PeriodStartUtc);
            Assert.Equal(newPeriodEnd, wallet.PeriodEndUtc);

            var ledgerEntries = await databaseContext.LedgerEntries
                .AsNoTracking()
                .Where(entry => entry.UserId == userId)
                .OrderBy(entry => entry.OccurredAtUtc)
                .ToListAsync();

            Assert.Equal(2, ledgerEntries.Count);

            var grantEntry = Assert.Single(ledgerEntries, entry => entry.Delta > 0);
            Assert.Equal("PeriodGrant", grantEntry.ActionType);
            Assert.Equal(10, grantEntry.Delta);

            var spendEntry = Assert.Single(ledgerEntries, entry => entry.Delta < 0);
            Assert.Equal(CreditActionType.AiTemplateEnqueued.ToString(), spendEntry.ActionType);
            Assert.Equal(-5, spendEntry.Delta);
        }
    }

    [Fact]
    public async Task TrySpend_WhenWalletPeriodMatchesSubscription_DoesNotGrantNewCredits()
    {
        await fixture.ResetDbAsync();

        const string channelId = "credits-same-period-channel";
        const string uploadsPlaylistId = "ULCreditsSamePeriod";
        const string userId = MockAuthenticationExtensions.TestSub;

        fixture.ApiFactory.MockCurrentChannelContext
            .Setup(channelContext => channelContext.GetRequiredChannelId())
            .Returns(channelId);

        var targetVideo = CreateTargetVideo(uploadsPlaylistId);

        var periodStart = TestFixture.TestingDateTimeOffset;
        var periodEnd = TestFixture.TestingDateTimeOffset.AddMonths(1);

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
                    "Credits Same Period Channel",
                    uploadsPlaylistId,
                    TestFixture.TestingDateTimeOffset),
                CancellationToken.None);

            databaseContext.Videos.Add(targetVideo);

            var plan = new Plan
            {
                Code = "CreditsSamePeriodPlan",
                Name = "Credits Same Period Plan",
                MonthlyCredits = 10,
                IsActive = true,
                CreatedAtUtc = TestFixture.TestingDateTimeOffset,
                UpdatedAtUtc = TestFixture.TestingDateTimeOffset
            };

            await databaseContext.Plans.AddAsync(plan, CancellationToken.None);
            await databaseContext.SaveChangesAsync(CancellationToken.None);

            // Create a wallet that matches the subscription period
            var existingWallet = new Wallet
            {
                UserId = userId,
                Balance = 7,
                PeriodStartUtc = periodStart,
                PeriodEndUtc = periodEnd,
                UpdatedAtUtc = TestFixture.TestingDateTimeOffset
            };

            await databaseContext.Wallets.AddAsync(existingWallet, CancellationToken.None);

            var subscription = new Subscription
            {
                UserId = userId,
                PlanId = plan.Id,
                PeriodStartUtc = periodStart,
                PeriodEndUtc = periodEnd,
                Status = SubscriptionStatus.Active
            };

            await databaseContext.Subscriptions.AddAsync(subscription, CancellationToken.None);

            var aiTemplateEnqueuedCost = new ActionCost
            {
                ActionType = CreditActionType.AiTemplateEnqueued.ToString(),
                Cost = 2,
                IsEnabled = true,
                UpdatedAtUtc = TestFixture.TestingDateTimeOffset,
                Notes = "Credits integration test cost for same period scenario."
            };

            await databaseContext.ActionCosts.AddAsync(aiTemplateEnqueuedCost, CancellationToken.None);
            await databaseContext.SaveChangesAsync(CancellationToken.None);
        }

        var request = new AiVideoTemplateRequest(
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
        requestMessage.Headers.Add("OperationId", OperationId);

        var response = await fixture.HttpClient.SendAsync(requestMessage);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using (var verificationScope = fixture.ApiServices.CreateScope())
        {
            var databaseContext = verificationScope.ServiceProvider.GetRequiredService<TubesterDb>();

            var wallet = await databaseContext.Wallets
                .AsNoTracking()
                .SingleOrDefaultAsync(entity => entity.UserId == userId);

            Assert.NotNull(wallet);
            // Should deduct from existing 7 credits, not grant new ones
            Assert.Equal(5, wallet!.Balance); // 7 - 2 = 5

            var ledgerEntries = await databaseContext.LedgerEntries
                .AsNoTracking()
                .Where(entry => entry.UserId == userId)
                .ToListAsync();

            // Only spend entry, no grant entry since wallet period matches subscription
            Assert.Single(ledgerEntries);

            var spendEntry = ledgerEntries[0];
            Assert.Equal(CreditActionType.AiTemplateEnqueued.ToString(), spendEntry.ActionType);
            Assert.Equal(-2, spendEntry.Delta);
        }
    }

    [Fact]
    public async Task AiReplyGenerated_WithSufficientCredits_DeductsCreditsAndAppendsLedgerEntry()
    {
        await fixture.ResetDbAsync();
        fixture.WorkerFactory.MockBackgroundYoutubeIntegration.Reset();
        fixture.WorkerFactory.MockAiClient.Reset();

        const string channelId = "credits-ai-reply-channel";
        const string uploadsPlaylistId = "ULCreditsAiReply";
        const string userId = MockAuthenticationExtensions.TestSub;
        const string videoId = "credits-ai-reply-video";
        const string commentId = "credits-ai-reply-comment";

        var video = Video.Create(
            uploadsPlaylistId,
            videoId,
            "Credits AI Reply Video",
            "Video description",
            TestFixture.TestingDateTimeOffset.AddDays(-1),
            TimeSpan.FromMinutes(5),
            VideoVisibility.Public,
            ["test"],
            "22",
            "en",
            "en",
            null,
            null,
            TestFixture.TestingDateTimeOffset,
            "etag-ai-reply",
            true
        );

        using (var serviceScope = fixture.WorkerServices.CreateScope())
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
                    "Credits AI Reply Channel",
                    uploadsPlaylistId,
                    TestFixture.TestingDateTimeOffset),
                CancellationToken.None);

            databaseContext.Videos.Add(video);

            var plan = new Plan
            {
                Code = "CreditsAiReplyPlan",
                Name = "Credits AI Reply Plan",
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

            var aiReplyGeneratedCost = new ActionCost
            {
                ActionType = CreditActionType.AiReplyGenerated.ToString(),
                Cost = 1,
                IsEnabled = true,
                UpdatedAtUtc = TestFixture.TestingDateTimeOffset,
                Notes = "Credits integration test cost for AiReplyGenerated."
            };

            await databaseContext.ActionCosts.AddAsync(aiReplyGeneratedCost, CancellationToken.None);
            await databaseContext.SaveChangesAsync(CancellationToken.None);
        }

        var commentThread = new Integration.Dtos.CommentThreadDto(
            commentId,
            videoId,
            "author-channel-id",
            "Great video! Can you explain more?",
            TestFixture.TestingDateTimeOffset.AddDays(-15));

        fixture.WorkerFactory.MockBackgroundYoutubeIntegration
            .Setup(integration => integration.GetUnansweredTopLevelCommentsAsync(
                channelId,
                videoId,
                It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnumerable([commentThread]));

        fixture.WorkerFactory.MockAiClient
            .Setup(client => client.SuggestReplyAsync(
                video.Title!,
                video.Tags,
                commentThread.Text,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("Thanks for watching! I'll cover that in a future video.");

        using (var setupScope = fixture.WorkerServices.CreateScope())
        {
            var databaseContext = setupScope.ServiceProvider.GetRequiredService<TubesterDb>();
            var channelSettings = Domain.ChannelSettings.CreateDefault(channelId, TestFixture.TestingDateTimeOffset);
            channelSettings.Apply(true, true, 10, 10, "English", null, TestFixture.TestingDateTimeOffset);
            databaseContext.ChannelSettings.Add(channelSettings);
            await databaseContext.SaveChangesAsync(CancellationToken.None);
        }

        using (var jobScope = fixture.WorkerServices.CreateScope())
        {
            var commentScanJob = jobScope.ServiceProvider.GetRequiredService<CommentScanJob>();
            await commentScanJob.Run(channelId, new Hangfire.JobCancellationToken(false));
        }

        using (var verificationScope = fixture.WorkerServices.CreateScope())
        {
            var databaseContext = verificationScope.ServiceProvider.GetRequiredService<TubesterDb>();

            var wallet = await databaseContext.Wallets
                .AsNoTracking()
                .SingleOrDefaultAsync(entity => entity.UserId == userId);

            Assert.NotNull(wallet);
            Assert.Equal(4, wallet!.Balance); // 5 monthly credits - 1 cost

            var ledgerEntries = await databaseContext.LedgerEntries
                .AsNoTracking()
                .Where(entry => entry.UserId == userId)
                .OrderBy(entry => entry.OccurredAtUtc)
                .ToListAsync();

            Assert.Equal(2, ledgerEntries.Count);
            Assert.Contains(ledgerEntries, entry => entry.ActionType == "PeriodGrant" && entry.Delta == 5);

            var spendEntry = Assert.Single(ledgerEntries, entry => entry.Delta < 0);
            Assert.Equal(CreditActionType.AiReplyGenerated.ToString(), spendEntry.ActionType);
            Assert.Equal(-1, spendEntry.Delta);
            Assert.Equal(commentId, spendEntry.ReferenceId);

            var reply = await databaseContext.Replies
                .AsNoTracking()
                .SingleOrDefaultAsync(entity => entity.CommentId == commentId);

            Assert.NotNull(reply);
            Assert.Equal("Thanks for watching! I'll cover that in a future video.", reply!.SuggestedText);
        }
    }

    [Fact]
    public async Task AiReplyGenerated_WhenAiClientFails_RefundsCredits()
    {
        await fixture.ResetDbAsync();
        fixture.WorkerFactory.MockBackgroundYoutubeIntegration.Reset();
        fixture.WorkerFactory.MockAiClient.Reset();

        const string channelId = "credits-ai-reply-refund-channel";
        const string uploadsPlaylistId = "ULCreditsAiReplyRefund";
        const string userId = MockAuthenticationExtensions.TestSub;
        const string videoId = "credits-ai-reply-refund-video";
        const string commentId = "credits-ai-reply-refund-comment";

        var video = Video.Create(
            uploadsPlaylistId,
            videoId,
            "Credits AI Reply Refund Video",
            "Video description",
            TestFixture.TestingDateTimeOffset.AddDays(-1),
            TimeSpan.FromMinutes(5),
            VideoVisibility.Public,
            ["test"],
            "22",
            "en",
            "en",
            null,
            null,
            TestFixture.TestingDateTimeOffset,
            "etag-ai-reply-refund",
            true
        );

        using (var serviceScope = fixture.WorkerServices.CreateScope())
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
                    "Credits AI Reply Refund Channel",
                    uploadsPlaylistId,
                    TestFixture.TestingDateTimeOffset),
                CancellationToken.None);

            databaseContext.Videos.Add(video);

            var plan = new Plan
            {
                Code = "CreditsAiReplyRefundPlan",
                Name = "Credits AI Reply Refund Plan",
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

            var aiReplyGeneratedCost = new ActionCost
            {
                ActionType = CreditActionType.AiReplyGenerated.ToString(),
                Cost = 2,
                IsEnabled = true,
                UpdatedAtUtc = TestFixture.TestingDateTimeOffset,
                Notes = "Credits integration test cost for AiReplyGenerated refund scenario."
            };

            await databaseContext.ActionCosts.AddAsync(aiReplyGeneratedCost, CancellationToken.None);
            await databaseContext.SaveChangesAsync(CancellationToken.None);
        }

        var commentThread = new Integration.Dtos.CommentThreadDto(
            commentId,
            videoId,
            "author-channel-id",
            "Great video! Can you explain more?",
            TestFixture.TestingDateTimeOffset.AddDays(-15));

        fixture.WorkerFactory.MockBackgroundYoutubeIntegration
            .Setup(integration => integration.GetUnansweredTopLevelCommentsAsync(
                channelId,
                videoId,
                It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnumerable([commentThread]));

        fixture.WorkerFactory.MockAiClient
            .Setup(client => client.SuggestReplyAsync(
                video.Title!,
                video.Tags,
                commentThread.Text,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Simulated AI client failure."));

        using (var setupScope = fixture.WorkerServices.CreateScope())
        {
            var databaseContext = setupScope.ServiceProvider.GetRequiredService<TubesterDb>();
            var channelSettings = Domain.ChannelSettings.CreateDefault(channelId, TestFixture.TestingDateTimeOffset);
            channelSettings.Apply(true, true, 10, 10, "English", null, TestFixture.TestingDateTimeOffset);
            databaseContext.ChannelSettings.Add(channelSettings);
            await databaseContext.SaveChangesAsync(CancellationToken.None);
        }

        using (var jobScope = fixture.WorkerServices.CreateScope())
        {
            var commentScanJob = jobScope.ServiceProvider.GetRequiredService<CommentScanJob>();
            await Assert.ThrowsAsync<Exception>(() =>
                commentScanJob.Run(channelId, new Hangfire.JobCancellationToken(false)));
        }

        using (var verificationScope = fixture.WorkerServices.CreateScope())
        {
            var databaseContext = verificationScope.ServiceProvider.GetRequiredService<TubesterDb>();

            var wallet = await databaseContext.Wallets
                .AsNoTracking()
                .SingleOrDefaultAsync(entity => entity.UserId == userId);

            Assert.NotNull(wallet);
            Assert.Equal(5, wallet!.Balance); // 5 monthly credits - 2 cost + 2 refund = 5

            var ledgerEntries = await databaseContext.LedgerEntries
                .AsNoTracking()
                .Where(entry => entry.UserId == userId)
                .OrderBy(entry => entry.OccurredAtUtc)
                .ToListAsync();

            Assert.Equal(3, ledgerEntries.Count);

            var grantEntry = Assert.Single(ledgerEntries, entry => entry.ActionType == "PeriodGrant");
            Assert.Equal(5, grantEntry.Delta);

            var spendEntry = Assert.Single(ledgerEntries, entry =>
                entry.ActionType == CreditActionType.AiReplyGenerated.ToString() && entry.Delta < 0);
            Assert.Equal(-2, spendEntry.Delta);

            var refundEntry = Assert.Single(ledgerEntries, entry =>
                entry.ActionType == CreditActionType.AiReplyGenerated.ToString() && entry.Delta > 0);
            Assert.Equal(2, refundEntry.Delta);
        }
    }

    [Fact]
    public async Task AiReplyGenerated_WithInsufficientCredits_SkipsCommentAndDoesNotDeductCredits()
    {
        await fixture.ResetDbAsync();
        fixture.WorkerFactory.MockBackgroundYoutubeIntegration.Reset();
        fixture.WorkerFactory.MockAiClient.Reset();

        const string channelId = "credits-ai-reply-insufficient-channel";
        const string uploadsPlaylistId = "ULCreditsAiReplyInsufficient";
        const string userId = MockAuthenticationExtensions.TestSub;
        const string videoId = "credits-ai-reply-insufficient-video";
        const string commentId = "credits-ai-reply-insufficient-comment";

        var video = Video.Create(
            uploadsPlaylistId,
            videoId,
            "Credits AI Reply Insufficient Video",
            "Video description",
            TestFixture.TestingDateTimeOffset.AddDays(-1),
            TimeSpan.FromMinutes(5),
            VideoVisibility.Public,
            ["test"],
            "22",
            "en",
            "en",
            null,
            null,
            TestFixture.TestingDateTimeOffset,
            "etag-ai-reply-insufficient",
            true
        );

        using (var serviceScope = fixture.WorkerServices.CreateScope())
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
                    "Credits AI Reply Insufficient Channel",
                    uploadsPlaylistId,
                    TestFixture.TestingDateTimeOffset),
                CancellationToken.None);

            databaseContext.Videos.Add(video);

            var plan = new Plan
            {
                Code = "CreditsAiReplyInsufficientPlan",
                Name = "Credits AI Reply Insufficient Plan",
                MonthlyCredits = 1,
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

            var aiReplyGeneratedCost = new ActionCost
            {
                ActionType = CreditActionType.AiReplyGenerated.ToString(),
                Cost = 5,
                IsEnabled = true,
                UpdatedAtUtc = TestFixture.TestingDateTimeOffset,
                Notes = "Credits integration test cost for AiReplyGenerated insufficient credits."
            };

            await databaseContext.ActionCosts.AddAsync(aiReplyGeneratedCost, CancellationToken.None);
            await databaseContext.SaveChangesAsync(CancellationToken.None);
        }

        var commentThread = new Integration.Dtos.CommentThreadDto(
            commentId,
            videoId,
            "author-channel-id",
            "Great video! Can you explain more?",
            TestFixture.TestingDateTimeOffset.AddDays(-15));

        fixture.WorkerFactory.MockBackgroundYoutubeIntegration
            .Setup(integration => integration.GetUnansweredTopLevelCommentsAsync(
                channelId,
                videoId,
                It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnumerable([commentThread]));

        // AI client should NOT be called because credits are insufficient
        fixture.WorkerFactory.MockAiClient
            .Setup(client => client.SuggestReplyAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("AI client should not be called."));

        using (var setupScope = fixture.WorkerServices.CreateScope())
        {
            var databaseContext = setupScope.ServiceProvider.GetRequiredService<TubesterDb>();
            var channelSettings = Domain.ChannelSettings.CreateDefault(channelId, TestFixture.TestingDateTimeOffset);
            channelSettings.Apply(true, true, 10, 10, "English", null, TestFixture.TestingDateTimeOffset);
            databaseContext.ChannelSettings.Add(channelSettings);
            await databaseContext.SaveChangesAsync(CancellationToken.None);
        }

        using (var jobScope = fixture.WorkerServices.CreateScope())
        {
            var commentScanJob = jobScope.ServiceProvider.GetRequiredService<CommentScanJob>();
            await commentScanJob.Run(channelId, new Hangfire.JobCancellationToken(false));
        }

        using (var verificationScope = fixture.WorkerServices.CreateScope())
        {
            var databaseContext = verificationScope.ServiceProvider.GetRequiredService<TubesterDb>();

            var wallet = await databaseContext.Wallets
                .AsNoTracking()
                .SingleOrDefaultAsync(entity => entity.UserId == userId);

            Assert.NotNull(wallet);
            Assert.Equal(1, wallet!.Balance); // Only granted monthly credit, no deduction

            var ledgerEntries = await databaseContext.LedgerEntries
                .AsNoTracking()
                .Where(entry => entry.UserId == userId)
                .ToListAsync();

            Assert.Single(ledgerEntries);
            Assert.Equal("PeriodGrant", ledgerEntries[0].ActionType);
            Assert.Equal(1, ledgerEntries[0].Delta);

            var reply = await databaseContext.Replies
                .AsNoTracking()
                .SingleOrDefaultAsync(entity => entity.CommentId == commentId);

            // Reply is created with Drafting status (insert-first approach)
            // but not updated to Suggested due to insufficient credits
            Assert.NotNull(reply);
            Assert.Equal(ReplyStatus.Drafting, reply!.Status);
            Assert.Null(reply.SuggestedText);
        }

        fixture.WorkerFactory.MockAiClient.Verify(
            client => client.SuggestReplyAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(IEnumerable<T> items)
    {
        foreach (var item in items)
        {
            yield return item;
        }

        await Task.CompletedTask;
    }

    private static Video CreateSourceVideo(string uploadsPlaylistId)
    {
        var video = Video.Create(
            uploadsPlaylistId,
            "credits-source-video",
            "Credits Source Video",
            "Credits Source Description",
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
            "etag-credits-source",
            true
        );

        return video;
    }
}
