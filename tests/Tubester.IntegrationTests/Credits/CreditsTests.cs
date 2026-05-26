using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Tubester.Abstractions.ApplicationConfiguration;
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

namespace Tubester.IntegrationTests.Credits;

[Collection(nameof(TestCollection))]
public sealed class CreditsTests(TestFixture fixture)
{
    private readonly TestHelpers _helpers = new(fixture.ApiServices);

    private const string OperationId = "credits-idempotency-operation";

    private const int TotalCost = TestConstants.AiTitleEnqueuedCost + TestConstants.AiDescriptionEnqueuedCost +
                                  TestConstants.AiTagsEnqueuedCost;

    [Fact]
    public async Task AiTemplateEnqueue_WithInsufficientCredits_ReturnsForbidden_AndDoesNotPersistWalletOrLedger()
    {
        await fixture.CleanStateAsync();
        const int credits = 0;
        await _helpers.SeedTestDataAsync(new TestDataOptions
        {
            MonthlyCredits = credits
        });

        var request = new AiVideoTemplateRequest
        {
            TargetVideoId = TestConstants.TargetVideoId,
            PromptEnrichment = "Generate better metadata for credits test",
            ExpectedCreditCost = TestConstants.AiTitleEnqueuedCost + TestConstants.AiDescriptionEnqueuedCost + TestConstants.AiTagsEnqueuedCost
        };

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, "/api/videos/ai-template")
        {
            Content = TestHelpers.CreateJsonContent(request)
        };
        requestMessage.Headers.Add("OperationId", OperationId);

        var response = await fixture.HttpClient.SendAsync(requestMessage);

        Assert.Equal(HttpStatusCode.PaymentRequired, response.StatusCode);

        var responseBody = await response.Content.ReadAsStringAsync();
        Assert.Contains("Insufficient credits to enqueue AI templating.", responseBody);
        await _helpers.AssertWalletIsNullAsync();
        await _helpers.AssertEmptyLedger();
    }

    [Fact]
    public async Task VideoDetailsSubmit_WithSufficientCredits_DeductsCreditsAndAppendsLedgerEntry()
    {
        await fixture.CleanStateAsync();
        var testData = await _helpers.SeedTestDataAsync();

        fixture.ApiFactory.MockYouTubeIntegration
            .Setup(youTubeIntegration => youTubeIntegration.UpdateVideoAsync(
                TestConstants.TargetVideoId,
                testData.Video!.Title!,
                testData.Video!.Description!,
                It.Is<IReadOnlyList<string>>(tags => tags.SequenceEqual(testData.Video!.Tags!)),
                TestConstants.TargetVideoCategoryId,
                TestConstants.DefaultLanguage,
                TestConstants.DefaultAudioLanguage,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var request = new UpdateVideoMetadataRequest(
            TestConstants.TargetVideoId);

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, "/api/videos/update")
        {
            Content = TestHelpers.CreateJsonContent(request)
        };
        requestMessage.Headers.Add("OperationId", OperationId);

        var response = await fixture.HttpClient.SendAsync(requestMessage);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await _helpers.AssertLedgerAfterDeductionAsync(nameof(CreditActionType.AiTemplateSubmitted), TestConstants.AiTemplateSubmittedCost,
            TestConstants.TargetVideoId, TestFixture.TestingDateTimeOffset);
    }

    [Fact]
    public async Task VideoDetailsSubmit_WithInsufficientCredits_ReturnsForbidden_AndDoesNotDeductCredits()
    {
        await fixture.CleanStateAsync();
        await _helpers.SeedTestDataAsync(new TestDataOptions
        {
            MonthlyCredits = 0
        });

        var request = new UpdateVideoMetadataRequest(
            TestConstants.TargetVideoId);

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, "/api/videos/update")
        {
            Content = TestHelpers.CreateJsonContent(request)
        };
        requestMessage.Headers.Add("OperationId", OperationId);

        var response = await fixture.HttpClient.SendAsync(requestMessage);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var responseBody = await response.Content.ReadAsStringAsync();
        Assert.Contains("Insufficient credits to submit AI template changes.", responseBody);

        await VerifyNoDeductionsAsync(0);
    }

    [Fact]
    public async Task ReplyPostedToYouTube_WithSufficientCredits_DeductsCreditsAndAppendsLedgerEntry()
    {
        await fixture.CleanStateAsync();
        fixture.ApiFactory.MockYouTubeIntegration.Reset();
        await _helpers.SeedTestDataAsync();

        var reply = Reply.Create(
            "credits-comment-1",
            TestConstants.TargetVideoId,
            "Test Video",
            "Test comment",
            TestFixture.TestingDateTimeOffset,
            TestFixture.TestingDateTimeOffset.AddDays(-1)
            );
        reply.SuggestText("Suggested text", TestFixture.TestingDateTimeOffset.AddMinutes(5));

        using (var serviceScope = fixture.ApiServices.CreateScope())
        {
            var databaseContext = serviceScope.ServiceProvider.GetRequiredService<TubesterDb>();
            databaseContext.Replies.Add(reply);
            await databaseContext.SaveChangesAsync(CancellationToken.None);
        }

        const string approvedText = "Approved reply text";

        fixture.ApiFactory.MockYouTubeIntegration
            .Setup(youTubeIntegration => youTubeIntegration.ReplyAsync(
                reply.CommentId,
                approvedText,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var batchRequest = new BatchDecisionRequest([new DraftDecisionDto
            {
                CommentId = reply.CommentId,
                ApprovedText = approvedText
            }
        ]);
        var requestMessage = new HttpRequestMessage(HttpMethod.Post, "/api/replies/approve")
        {
            Content = TestHelpers.CreateJsonContent(batchRequest)
        };
        requestMessage.Headers.Add("OperationId", OperationId);

        var response = await fixture.HttpClient.SendAsync(requestMessage);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await _helpers.AssertLedgerAfterDeductionAsync(nameof(CreditActionType.ReplyPostedToYouTube),
            TestConstants.ReplyPostedActionCost, reply.CommentId);
    }

    [Fact]
    public async Task ReplyPostedToYouTube_WithInsufficientCredits_FailsWithInsufficientCreditsMessage()
    {
        await fixture.CleanStateAsync();
        fixture.ApiFactory.MockYouTubeIntegration.Reset();
        await _helpers.SeedTestDataAsync(new TestDataOptions { MonthlyCredits = 0 });

        var reply = Reply.Create(
            "credits-insufficient-comment-1",
            TestConstants.TargetVideoId,
            "Test Video",
            "Test comment",
            TestFixture.TestingDateTimeOffset,
            TestFixture.TestingDateTimeOffset.AddDays(-1));
        reply.SuggestText("Suggested text", TestFixture.TestingDateTimeOffset.AddMinutes(5));

        using var serviceScope = fixture.ApiServices.CreateScope();
        var databaseContext = serviceScope.ServiceProvider.GetRequiredService<TubesterDb>();
        databaseContext.Replies.Add(reply);
        await databaseContext.SaveChangesAsync(CancellationToken.None);

        var request = new BatchDecisionRequest([
            new DraftDecisionDto { CommentId = reply.CommentId, ApprovedText = "Approved text" }
        ]);
        var requestMessage = new HttpRequestMessage(HttpMethod.Post, "/api/replies/approve")
        {
            Content = TestHelpers.CreateJsonContent(request)
        };
        requestMessage.Headers.Add("OperationId", OperationId);

        var response = await fixture.HttpClient.SendAsync(requestMessage);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await TestHelpers.DeserializeAsync<BatchDecisionResultDto>(response);

        Assert.NotNull(result);
        Assert.Equal(1, result.Total);
        Assert.Equal(0, result.Succeeded);
        Assert.Equal(1, result.Failed);
        Assert.Contains(result.Items,
            item => item.CommentId == reply.CommentId && !item.Success && item.Error == "Insufficient credits.");

        await VerifyNoDeductionsAsync(0);
    }

    [Fact]
    public async Task TrySpend_WhenUserResubscribesWithNewPeriod_GrantsFreshCredits()
    {
        await fixture.CleanStateAsync();
        await _helpers.SeedTestDataAsync(new TestDataOptions
        {
            CreateSubscription = false
        });

        // Old subscription period: Jan 15 - Feb 15 (still valid on Feb 10)
        var oldPeriodStart = TestFixture.TestingDateTimeOffset.AddDays(-10);
        var oldPeriodEnd = TestFixture.TestingDateTimeOffset.AddDays(20);

        // New subscription period: Feb 5 - Mar 5 (user resubscribed)
        var newPeriodStart = TestFixture.TestingDateTimeOffset.AddDays(-5);
        var newPeriodEnd = TestFixture.TestingDateTimeOffset.AddDays(25);

        using (var serviceScope = fixture.ApiServices.CreateScope())
        {
            var databaseContext = serviceScope.ServiceProvider.GetRequiredService<TubesterDb>();

            var plan = new Plan
            {
                Code = "CreditsResubscribePlan",
                Name = "Credits Resubscribe Plan",
                MonthlyCredits = TestConstants.MonthlyCredits,
                IsActive = true,
                CreatedAtUtc = TestFixture.TestingDateTimeOffset,
                UpdatedAtUtc = TestFixture.TestingDateTimeOffset
            };

            await databaseContext.Plans.AddAsync(plan, CancellationToken.None);
            await databaseContext.SaveChangesAsync(CancellationToken.None);

            // Create a wallet from the OLD subscription period with only 2 credits remaining
            var oldWallet = new Wallet
            {
                UserId = TestConstants.UserId,
                Balance = 2,
                PeriodStartUtc = oldPeriodStart,
                PeriodEndUtc = oldPeriodEnd,
                UpdatedAtUtc = TestFixture.TestingDateTimeOffset.AddDays(-5)
            };

            await databaseContext.Wallets.AddAsync(oldWallet, CancellationToken.None);

            // Create a NEW subscription with different period dates (user resubscribed)
            var newSubscription = new Subscription
            {
                UserId = TestConstants.UserId,
                PlanId = plan.Id,
                PeriodStartUtc = newPeriodStart,
                PeriodEndUtc = newPeriodEnd,
                Status = SubscriptionStatus.Active
            };

            await databaseContext.Subscriptions.AddAsync(newSubscription, CancellationToken.None);
            await databaseContext.SaveChangesAsync(CancellationToken.None);
        }

        var request = new AiVideoTemplateRequest
        {
            TargetVideoId = TestConstants.TargetVideoId,
            PromptEnrichment = "Generate better metadata",
            ExpectedCreditCost = TestConstants.AiTitleEnqueuedCost + TestConstants.AiDescriptionEnqueuedCost + TestConstants.AiTagsEnqueuedCost
        };

        var requestContent = TestHelpers.CreateJsonContent(request);

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, "/api/videos/ai-template")
        {
            Content = requestContent
        };
        requestMessage.Headers.Add("OperationId", OperationId);

        var response = await fixture.HttpClient.SendAsync(requestMessage);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        await _helpers.AssertLedgerAfterBatchDeductionAsync(true, true, true, false,
            TestConstants.TargetVideoId, TestFixture.TestingDateTimeOffset);
        await _helpers.AssertWalletAsync(TestConstants.AiTitleEnqueuedCost + TestConstants.AiDescriptionEnqueuedCost + TestConstants.AiTagsEnqueuedCost);
    }


    [Fact]
    public async Task TrySpend_WhenWalletPeriodMatchesSubscription_DoesNotGrantNewCredits()
    {
        await fixture.CleanStateAsync();
        await _helpers.SeedTestDataAsync(new TestDataOptions
        {
            CreateSubscription = false,
        });

        var periodStart = TestFixture.TestingDateTimeOffset.AddDays(-15);
        var periodEnd = TestFixture.TestingDateTimeOffset.AddDays(15);
        var oldGrantAt = periodStart;

        using var serviceScope = fixture.ApiServices.CreateScope();
        var databaseContext = serviceScope.ServiceProvider.GetRequiredService<TubesterDb>();
        var plan = new Plan
        {
            Code = "CreditsSamePeriodPlan",
            Name = "Credits Same Period Plan",
            MonthlyCredits = TestConstants.MonthlyCredits,
            IsActive = true,
            CreatedAtUtc = oldGrantAt,
            UpdatedAtUtc = oldGrantAt
        };

        await databaseContext.Plans.AddAsync(plan, CancellationToken.None);
        await databaseContext.SaveChangesAsync(CancellationToken.None);

        var subscription = new Subscription
        {
            UserId = TestConstants.UserId,
            PlanId = plan.Id,
            PeriodStartUtc = periodStart,
            PeriodEndUtc = periodEnd,
            Status = SubscriptionStatus.Active
        };

        await databaseContext.Subscriptions.AddAsync(subscription, CancellationToken.None);
        await databaseContext.SaveChangesAsync(CancellationToken.None);

        var creditStore = serviceScope.ServiceProvider.GetRequiredService<ICreditsStore>();
        var idempotencyKey = $"grant:{TestConstants.UserId}:{TestFixture.TestingDateTimeOffset.ToUniversalTime():O}";
        await creditStore.GrantPeriodCreditsAsync(TestConstants.UserId, subscription.PeriodStartUtc,
            subscription.PeriodEndUtc, TestConstants.MonthlyCredits, idempotencyKey, oldGrantAt, CancellationToken.None);

        var request = new AiVideoTemplateRequest
        {
            TargetVideoId = TestConstants.TargetVideoId,
            PromptEnrichment = "Generate better metadata",
            ExpectedCreditCost = TestConstants.AiTitleEnqueuedCost + TestConstants.AiDescriptionEnqueuedCost + TestConstants.AiTagsEnqueuedCost
        };

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, "/api/videos/ai-template")
        {
            Content = TestHelpers.CreateJsonContent(request)
        };
        requestMessage.Headers.Add("OperationId", OperationId);

        var response = await fixture.HttpClient.SendAsync(requestMessage);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        await _helpers.AssertLedgerAfterBatchDeductionAsync(true, true, true, false,
            TestConstants.TargetVideoId, oldGrantAt);
        await _helpers.AssertWalletAsync(TestConstants.AiTitleEnqueuedCost + TestConstants.AiDescriptionEnqueuedCost + TestConstants.AiTagsEnqueuedCost);
    }

    [Fact]
    public async Task AiReplyGenerated_WithSufficientCredits_DeductsCreditsAndAppendsLedgerEntry()
    {
        await fixture.CleanStateAsync();
        fixture.WorkerFactory.MockBackgroundYoutubeIntegration.Reset();


        var video = TestHelpers.GetTargetVideo();
        await _helpers.SeedTestDataAsync(new TestDataOptions
        {
            Videos = [video]
        });

        const string commentId = "credits-ai-reply-comment";

        var commentThread = new Integration.Dtos.CommentThreadDto(
            commentId,
            TestConstants.TargetVideoId,
            "author-channel-id",
            "Great video! Can you explain more?",
            TestFixture.TestingDateTimeOffset.AddDays(-9));

        fixture.WorkerFactory.MockBackgroundYoutubeIntegration
            .Setup(integration => integration.GetUnansweredTopLevelCommentsAsync(
                TestConstants.ChannelId,
                TestConstants.TargetVideoId,
                It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnumerable([commentThread]));

        var jsonResponse = JsonSerializer.Serialize(new { reply = "Thanks for watching! I'll cover that in a future video." });
        fixture.WorkerFactory.MockAiTextGenerationClient
            .Setup(client => client.GenerateTextAsync(
                AiOperation.Reply,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateMockResult(jsonResponse));

        using (var jobScope = fixture.WorkerServices.CreateScope())
        {
            var commentScanJob = jobScope.ServiceProvider.GetRequiredService<CommentScanJob>();
            await commentScanJob.Run(TestConstants.ChannelId, new Hangfire.JobCancellationToken(false));
        }

        await _helpers.AssertLedgerAfterDeductionAsync(nameof(CreditActionType.AiReplyGenerated),
            TestConstants.AiReplyGeneratedCost, commentId);

        using var verificationScope = fixture.WorkerServices.CreateScope();
        var databaseContext = verificationScope.ServiceProvider.GetRequiredService<TubesterDb>();
        var reply = await databaseContext.Replies
            .AsNoTracking()
            .SingleOrDefaultAsync(entity => entity.CommentId == commentId);

        Assert.NotNull(reply);
        Assert.Equal("Thanks for watching! I'll cover that in a future video.", reply.SuggestedText);
    }

    [Fact]
    public async Task AiReplyGenerated_WhenAiTextGenerationClientFails_RefundsCredits()
    {
        await fixture.CleanStateAsync();


        var video = TestHelpers.GetTargetVideo();
        await _helpers.SeedTestDataAsync(new TestDataOptions
        {
            Videos = [video]
        });

        const string commentId = "credits-ai-reply-comment";

        var commentThread = new Integration.Dtos.CommentThreadDto(
            commentId,
            TestConstants.TargetVideoId,
            "author-channel-id",
            "Great video! Can you explain more?",
            TestFixture.TestingDateTimeOffset.AddDays(-9));

        fixture.WorkerFactory.MockBackgroundYoutubeIntegration
            .Setup(integration => integration.GetUnansweredTopLevelCommentsAsync(
                TestConstants.ChannelId,
                TestConstants.TargetVideoId,
                It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnumerable([commentThread]));

        fixture.WorkerFactory.MockAiTextGenerationClient
            .Setup(client => client.GenerateTextAsync(
                AiOperation.Reply,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Simulated AI client failure."));

        using (var jobScope = fixture.WorkerServices.CreateScope())
        {
            var commentScanJob = jobScope.ServiceProvider.GetRequiredService<CommentScanJob>();
            await Assert.ThrowsAsync<Exception>(() =>
                commentScanJob.Run(TestConstants.ChannelId, new Hangfire.JobCancellationToken(false)));
        }

        using var verificationScope = fixture.WorkerServices.CreateScope();
        var databaseContext = verificationScope.ServiceProvider.GetRequiredService<TubesterDb>();

        var wallet = await databaseContext.Wallets
            .AsNoTracking()
            .SingleOrDefaultAsync(entity => entity.UserId == TestConstants.UserId);

        Assert.NotNull(wallet);
        Assert.Equal(TestConstants.MonthlyCredits, wallet.Balance); // 5 monthly credits - 2 cost + 2 refund = 5

        var ledgerEntries = await databaseContext.LedgerEntries
            .AsNoTracking()
            .Where(entry => entry.UserId == TestConstants.UserId)
            .OrderBy(entry => entry.OccurredAtUtc)
            .ToListAsync();

        Assert.Equal(3, ledgerEntries.Count);

        var grantEntry = Assert.Single(ledgerEntries, entry => entry.ActionType == "PeriodGrant");
        Assert.Equal(TestConstants.MonthlyCredits, grantEntry.Delta);

        var spendEntry = Assert.Single(ledgerEntries, entry =>
            entry.ActionType == nameof(CreditActionType.AiReplyGenerated) && entry.Delta < 0);
        Assert.Equal(-TestConstants.AiReplyGeneratedCost, spendEntry.Delta);

        var refundEntry = Assert.Single(ledgerEntries, entry =>
            entry.ActionType == nameof(CreditActionType.AiReplyGenerated) && entry.Delta > 0);
        Assert.Equal(TestConstants.AiReplyGeneratedCost, refundEntry.Delta);
    }

    [Fact]
    public async Task AiReplyGenerated_WithInsufficientCredits_SkipsCommentAndDoesNotDeductCredits()
    {
        await fixture.CleanStateAsync();
        fixture.WorkerFactory.MockBackgroundYoutubeIntegration.Reset();


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

            await databaseContext.SaveChangesAsync(CancellationToken.None);
        }

        var commentThread = new Integration.Dtos.CommentThreadDto(
            commentId,
            videoId,
            "author-channel-id",
            "Great video! Can you explain more?",
            TestFixture.TestingDateTimeOffset.AddDays(-9));

        fixture.WorkerFactory.MockBackgroundYoutubeIntegration
            .Setup(integration => integration.GetUnansweredTopLevelCommentsAsync(
                channelId,
                videoId,
                It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnumerable([commentThread]));

        // AI text generation client should NOT be called because credits are insufficient
        fixture.WorkerFactory.MockAiTextGenerationClient
            .Setup(client => client.GenerateTextAsync(
                It.IsAny<AiOperation>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("AI text generation client should not be called."));

        using (var setupScope = fixture.WorkerServices.CreateScope())
        {
            var databaseContext = setupScope.ServiceProvider.GetRequiredService<TubesterDb>();
            var channelSettings = ChannelSettings.CreateDefault(channelId, TestFixture.TestingDateTimeOffset);
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
            Assert.Equal(1, wallet.Balance); // Only granted monthly credit, no deduction

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
            Assert.Equal(ReplyStatus.Drafting, reply.Status);
            Assert.Null(reply.SuggestedText);
        }

        fixture.WorkerFactory.MockAiTextGenerationClient.Verify(
            client => client.GenerateTextAsync(
                It.IsAny<AiOperation>(),
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

    private async Task VerifyNoDeductionsAsync(int monthlyCredits = TestConstants.MonthlyCredits)
    {
        using var verificationScope = fixture.ApiServices.CreateScope();
        var databaseContext = verificationScope.ServiceProvider.GetRequiredService<TubesterDb>();

        var wallet = await databaseContext.Wallets
            .AsNoTracking()
            .SingleOrDefaultAsync(entity => entity.UserId == TestConstants.UserId);
        Assert.NotNull(wallet);
        Assert.Equal(monthlyCredits, wallet.Balance); //only granted monthly credit

        var ledgerEntries = await databaseContext.LedgerEntries
            .AsNoTracking()
            .Where(entry => entry.UserId == TestConstants.UserId)
            .ToListAsync();
        Assert.NotNull(ledgerEntries);
        Assert.Single(ledgerEntries);
        Assert.Equal(monthlyCredits, ledgerEntries[0].Delta);
    }

    [Fact]
    public async Task AiTemplate_WhenSubscriptionExpired_Renew()
    {
        // Arrange
        // User had an active subscription 2 months prior to TestingDateTimeOffset (April 1, 2026)
        // The subscription period ended 2 months ago (February 1, 2026)
        var expiredPeriodStart = TestFixture.TestingDateTimeOffset.AddMonths(-3).AddDays(-5);
        var expiredPeriodEnd = TestFixture.TestingDateTimeOffset.AddMonths(-2).AddDays(-5); // Period ended 2 months ago

        await fixture.CleanStateAsync();
        var testData = await _helpers.SeedTestDataAsync(new TestDataOptions
        {
            CreateSubscription = false
        });

        using var serviceScope = fixture.ApiServices.CreateScope();
        var databaseContext = serviceScope.ServiceProvider.GetRequiredService<TubesterDb>();
        // Create an EXPIRED subscription (period ended 2 months ago)
        var expiredSubscription = new Subscription
        {
            UserId = TestConstants.UserId,
            PlanId = testData.Plan!.Id,
            PeriodStartUtc = expiredPeriodStart,
            PeriodEndUtc = expiredPeriodEnd,
            Status = SubscriptionStatus.Active // Still active but period ended
        };

        await databaseContext.Subscriptions.AddAsync(expiredSubscription, CancellationToken.None);

        // Create a wallet from the expired subscription period with remaining credits
        var expiredWallet = new Wallet
        {
            UserId = TestConstants.UserId,
            Balance = 3, // Some remaining credits from old period
            PeriodStartUtc = expiredPeriodStart,
            PeriodEndUtc = expiredPeriodEnd,
            UpdatedAtUtc = expiredPeriodEnd
        };

        await databaseContext.Wallets.AddAsync(expiredWallet, CancellationToken.None);
        await databaseContext.SaveChangesAsync(CancellationToken.None);

        var request = new AiVideoTemplateRequest
        {
            TargetVideoId = TestConstants.TargetVideoId,
            PromptEnrichment = "Generate better metadata",
            ExpectedCreditCost = TestConstants.AiTitleEnqueuedCost + TestConstants.AiDescriptionEnqueuedCost + TestConstants.AiTagsEnqueuedCost
        };

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, "/api/videos/ai-template")
        {
            Content = TestHelpers.CreateJsonContent(request)
        };
        requestMessage.Headers.Add("OperationId", OperationId);

        // Act
        var response = await fixture.HttpClient.SendAsync(requestMessage);

        // Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        // Verify that fresh credits were granted and the spend was deducted
        await _helpers.AssertLedgerAfterBatchDeductionAsync(
            true, true, true, false,
            TestConstants.TargetVideoId,
            TestFixture.TestingDateTimeOffset); // Fresh grant should be at TestingDateTimeOffset
        await _helpers.AssertWalletAsync(TestConstants.AiTitleEnqueuedCost + TestConstants.AiDescriptionEnqueuedCost + TestConstants.AiTagsEnqueuedCost);

        // Verify subscription was correctly extended and history record was inserted
        using var verificationScope = fixture.ApiServices.CreateScope();
        var verificationContext = verificationScope.ServiceProvider.GetRequiredService<TubesterDb>();

        // Verify the subscription has been extended with new period dates
        var updatedSubscription = await verificationContext.Subscriptions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == TestConstants.UserId);

        Assert.NotNull(updatedSubscription);
        var expectedPeriodStart = expiredPeriodStart.AddMonths(3);
        var expectedPeriodEnd = expiredPeriodEnd.AddMonths(3);
        Assert.Equal(expectedPeriodStart, updatedSubscription.PeriodStartUtc);
        Assert.Equal(expectedPeriodEnd, updatedSubscription.PeriodEndUtc);

        // Verify the expired period was recorded in history
        var historyRecord = await verificationContext.SubscriptionHistories
            .AsNoTracking()
            .Include(h => h.Plan)
            .FirstOrDefaultAsync(h => h.UserId == TestConstants.UserId);

        Assert.NotNull(historyRecord);
        Assert.Equal(TestConstants.UserId, historyRecord.UserId);
        Assert.Equal(testData.Plan!.Id, historyRecord.PlanId);
        Assert.Equal(expiredPeriodStart, historyRecord.PeriodStartUtc);
        Assert.Equal(expiredPeriodEnd, historyRecord.PeriodEndUtc);
        Assert.Equal(SubscriptionStatus.Active, historyRecord.Status);
        Assert.Equal(TestFixture.TestingDateTimeOffset, historyRecord.CreatedAtUtc);
    }

    [Fact]
    public async Task AiTemplate_WhenSubscriptionExpiredForManyMonths_RenewsToCurrentAnchoredPeriod()
    {
        // Arrange
        await fixture.CleanStateAsync();

        var testData = await _helpers.SeedTestDataAsync(new TestDataOptions
        {
            CreateSubscription = false
        });

        var expiredPeriodStart = TestFixture.TestingDateTimeOffset.AddMonths(-7);
        var expiredPeriodEnd = TestFixture.TestingDateTimeOffset.AddMonths(-6);

        using var serviceScope = fixture.ApiServices.CreateScope();
        var databaseContext = serviceScope.ServiceProvider.GetRequiredService<TubesterDb>();

        databaseContext.Subscriptions.Add(new Subscription
        {
            UserId = TestConstants.UserId,
            PlanId = testData.Plan!.Id,
            PeriodStartUtc = expiredPeriodStart,
            PeriodEndUtc = expiredPeriodEnd,
            Status = SubscriptionStatus.Active
        });

        databaseContext.Wallets.Add(new Wallet
        {
            UserId = TestConstants.UserId,
            Balance = 3,
            PeriodStartUtc = expiredPeriodStart,
            PeriodEndUtc = expiredPeriodEnd,
            UpdatedAtUtc = expiredPeriodEnd
        });

        await databaseContext.SaveChangesAsync();

        var request = new AiVideoTemplateRequest
        {
            TargetVideoId = TestConstants.TargetVideoId,
            PromptEnrichment = "Generate better metadata",
            ExpectedCreditCost = TestConstants.AiTitleEnqueuedCost + TestConstants.AiDescriptionEnqueuedCost + TestConstants.AiTagsEnqueuedCost
        };

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, "/api/videos/ai-template")
        {
            Content = TestHelpers.CreateJsonContent(request)
        };
        requestMessage.Headers.Add("OperationId", OperationId);

        // Act
        var response = await fixture.HttpClient.SendAsync(requestMessage);

        // Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        using var verificationScope = fixture.ApiServices.CreateScope();
        var verificationContext = verificationScope.ServiceProvider.GetRequiredService<TubesterDb>();

        var updatedSubscription = await verificationContext.Subscriptions
            .AsNoTracking()
            .SingleAsync(subscription => subscription.UserId == TestConstants.UserId);

        Assert.Equal(TestFixture.TestingDateTimeOffset, updatedSubscription.PeriodStartUtc);
        Assert.Equal(TestFixture.TestingDateTimeOffset.AddMonths(1), updatedSubscription.PeriodEndUtc);

        var wallet = await verificationContext.Wallets
            .AsNoTracking()
            .SingleAsync(wallet => wallet.UserId == TestConstants.UserId);

        Assert.Equal(TestFixture.TestingDateTimeOffset, wallet.PeriodStartUtc);
        Assert.Equal(TestFixture.TestingDateTimeOffset.AddMonths(1), wallet.PeriodEndUtc);
        Assert.Equal(testData.Plan.MonthlyCredits - TotalCost, wallet.Balance);

        var historyRecords = await verificationContext.SubscriptionHistories
            .AsNoTracking()
            .Where(history => history.UserId == TestConstants.UserId)
            .ToListAsync();

        var historyRecord = Assert.Single(historyRecords);
        Assert.Equal(expiredPeriodStart, historyRecord.PeriodStartUtc);
        Assert.Equal(expiredPeriodEnd, historyRecord.PeriodEndUtc);
    }

    [Fact]
    public async Task AiTemplate_WhenSubscriptionExpiredButInactive_DoesNotRenew()
    {
        // Arrange
        await fixture.CleanStateAsync();

        var testData = await _helpers.SeedTestDataAsync(new TestDataOptions
        {
            CreateSubscription = false
        });

        var expiredPeriodStart = TestFixture.TestingDateTimeOffset.AddMonths(-2);
        var expiredPeriodEnd = TestFixture.TestingDateTimeOffset.AddMonths(-1);

        using var serviceScope = fixture.ApiServices.CreateScope();
        var databaseContext = serviceScope.ServiceProvider.GetRequiredService<TubesterDb>();

        databaseContext.Subscriptions.Add(new Subscription
        {
            UserId = TestConstants.UserId,
            PlanId = testData.Plan!.Id,
            PeriodStartUtc = expiredPeriodStart,
            PeriodEndUtc = expiredPeriodEnd,
            Status = SubscriptionStatus.Cancelled
        });

        databaseContext.Wallets.Add(new Wallet
        {
            UserId = TestConstants.UserId,
            Balance = testData.Plan.MonthlyCredits,
            PeriodStartUtc = expiredPeriodStart,
            PeriodEndUtc = expiredPeriodEnd,
            UpdatedAtUtc = expiredPeriodEnd
        });

        await databaseContext.SaveChangesAsync();

        var request = new AiVideoTemplateRequest
        {
            TargetVideoId = TestConstants.TargetVideoId,
            PromptEnrichment = "Generate better metadata"
        };

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, "/api/videos/ai-template")
        {
            Content = TestHelpers.CreateJsonContent(request)
        };
        requestMessage.Headers.Add("OperationId", OperationId);

        // Act
        var response = await fixture.HttpClient.SendAsync(requestMessage);

        // Assert
        Assert.Equal(HttpStatusCode.PaymentRequired, response.StatusCode);

        using var verificationScope = fixture.ApiServices.CreateScope();
        var verificationContext = verificationScope.ServiceProvider.GetRequiredService<TubesterDb>();

        var subscription = await verificationContext.Subscriptions
            .AsNoTracking()
            .SingleAsync(subscription => subscription.UserId == TestConstants.UserId);

        Assert.Equal(expiredPeriodStart, subscription.PeriodStartUtc);
        Assert.Equal(expiredPeriodEnd, subscription.PeriodEndUtc);

        Assert.Empty(await verificationContext.SubscriptionHistories
            .AsNoTracking()
            .Where(history => history.UserId == TestConstants.UserId)
            .ToListAsync());

        Assert.Empty(await verificationContext.LedgerEntries
            .AsNoTracking()
            .Where(entry => entry.UserId == TestConstants.UserId)
            .ToListAsync());
    }

    [Fact]
    public async Task AiTemplate_WhenSubscriptionExpiredAndPlanInactive_DoesNotRenew()
    {
        // Arrange
        await fixture.CleanStateAsync();

        var testData = await _helpers.SeedTestDataAsync(new TestDataOptions
        {
            CreateSubscription = false
        });

        var expiredPeriodStart = TestFixture.TestingDateTimeOffset.AddMonths(-2);
        var expiredPeriodEnd = TestFixture.TestingDateTimeOffset.AddMonths(-1);

        using var serviceScope = fixture.ApiServices.CreateScope();
        var databaseContext = serviceScope.ServiceProvider.GetRequiredService<TubesterDb>();

        var plan = await databaseContext.Plans
            .SingleAsync(plan => plan.Id == testData.Plan!.Id);

        plan.IsActive = false;

        databaseContext.Subscriptions.Add(new Subscription
        {
            UserId = TestConstants.UserId,
            PlanId = plan.Id,
            PeriodStartUtc = expiredPeriodStart,
            PeriodEndUtc = expiredPeriodEnd,
            Status = SubscriptionStatus.Active
        });

        databaseContext.Wallets.Add(new Wallet
        {
            UserId = TestConstants.UserId,
            Balance = plan.MonthlyCredits,
            PeriodStartUtc = expiredPeriodStart,
            PeriodEndUtc = expiredPeriodEnd,
            UpdatedAtUtc = expiredPeriodEnd
        });

        await databaseContext.SaveChangesAsync();

        var request = new AiVideoTemplateRequest
        {
            TargetVideoId = TestConstants.TargetVideoId,
            PromptEnrichment = "Generate better metadata"
        };

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, "/api/videos/ai-template")
        {
            Content = TestHelpers.CreateJsonContent(request)
        };
        requestMessage.Headers.Add("OperationId", OperationId);

        // Act
        var response = await fixture.HttpClient.SendAsync(requestMessage);

        // Assert
        Assert.Equal(HttpStatusCode.PaymentRequired, response.StatusCode);

        using var verificationScope = fixture.ApiServices.CreateScope();
        var verificationContext = verificationScope.ServiceProvider.GetRequiredService<TubesterDb>();

        var subscription = await verificationContext.Subscriptions
            .AsNoTracking()
            .SingleAsync(subscription => subscription.UserId == TestConstants.UserId);

        Assert.Equal(expiredPeriodStart, subscription.PeriodStartUtc);
        Assert.Equal(expiredPeriodEnd, subscription.PeriodEndUtc);

        Assert.Empty(await verificationContext.SubscriptionHistories
            .AsNoTracking()
            .Where(history => history.UserId == TestConstants.UserId)
            .ToListAsync());
    }

    [Fact]
    public async Task AiTemplate_WhenSubscriptionRenewed_DoesNotRollOverOldWalletBalance()
    {
        // Arrange
        await fixture.CleanStateAsync();

        var testData = await _helpers.SeedTestDataAsync(new TestDataOptions
        {
            CreateSubscription = false
        });

        var expiredPeriodStart = TestFixture.TestingDateTimeOffset.AddMonths(-2);
        var expiredPeriodEnd = TestFixture.TestingDateTimeOffset.AddMonths(-1);

        using var serviceScope = fixture.ApiServices.CreateScope();
        var databaseContext = serviceScope.ServiceProvider.GetRequiredService<TubesterDb>();

        databaseContext.Subscriptions.Add(new Subscription
        {
            UserId = TestConstants.UserId,
            PlanId = testData.Plan!.Id,
            PeriodStartUtc = expiredPeriodStart,
            PeriodEndUtc = expiredPeriodEnd,
            Status = SubscriptionStatus.Active
        });

        databaseContext.Wallets.Add(new Wallet
        {
            UserId = TestConstants.UserId,
            Balance = 999,
            PeriodStartUtc = expiredPeriodStart,
            PeriodEndUtc = expiredPeriodEnd,
            UpdatedAtUtc = expiredPeriodEnd
        });

        await databaseContext.SaveChangesAsync();

        var request = new AiVideoTemplateRequest
        {
            TargetVideoId = TestConstants.TargetVideoId,
            PromptEnrichment = "Generate better metadata",
            ExpectedCreditCost = TestConstants.AiTitleEnqueuedCost + TestConstants.AiDescriptionEnqueuedCost + TestConstants.AiTagsEnqueuedCost
        };

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, "/api/videos/ai-template")
        {
            Content = TestHelpers.CreateJsonContent(request)
        };
        requestMessage.Headers.Add("OperationId", OperationId);

        // Act
        var response = await fixture.HttpClient.SendAsync(requestMessage);

        // Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        using var verificationScope = fixture.ApiServices.CreateScope();
        var verificationContext = verificationScope.ServiceProvider.GetRequiredService<TubesterDb>();

        var wallet = await verificationContext.Wallets
            .AsNoTracking()
            .SingleAsync(wallet => wallet.UserId == TestConstants.UserId);

        Assert.Equal(TestFixture.TestingDateTimeOffset, wallet.PeriodStartUtc);
        Assert.Equal(TestFixture.TestingDateTimeOffset.AddMonths(1), wallet.PeriodEndUtc);

        Assert.Equal(
            testData.Plan!.MonthlyCredits - TotalCost,
            wallet.Balance);
    }

    [Fact]
    public async Task AiTemplate_WhenSubscriptionRenewedAndRequestRetried_DoesNotDoubleSpend()
    {
        // Arrange
        await fixture.CleanStateAsync();

        var testData = await _helpers.SeedTestDataAsync(new TestDataOptions
        {
            CreateSubscription = false
        });

        var expiredPeriodStart = TestFixture.TestingDateTimeOffset.AddMonths(-3);
        var expiredPeriodEnd = TestFixture.TestingDateTimeOffset.AddMonths(-2);

        using var serviceScope = fixture.ApiServices.CreateScope();
        var databaseContext = serviceScope.ServiceProvider.GetRequiredService<TubesterDb>();

        databaseContext.Subscriptions.Add(new Subscription
        {
            UserId = TestConstants.UserId,
            PlanId = testData.Plan!.Id,
            PeriodStartUtc = expiredPeriodStart,
            PeriodEndUtc = expiredPeriodEnd,
            Status = SubscriptionStatus.Active
        });

        databaseContext.Wallets.Add(new Wallet
        {
            UserId = TestConstants.UserId,
            Balance = 3,
            PeriodStartUtc = expiredPeriodStart,
            PeriodEndUtc = expiredPeriodEnd,
            UpdatedAtUtc = expiredPeriodEnd
        });

        await databaseContext.SaveChangesAsync();

        var request = new AiVideoTemplateRequest
        {
            TargetVideoId = TestConstants.TargetVideoId,
            PromptEnrichment = "Generate better metadata",
            ExpectedCreditCost = TestConstants.AiTitleEnqueuedCost + TestConstants.AiDescriptionEnqueuedCost + TestConstants.AiTagsEnqueuedCost
        };

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
        var response2 = await fixture.HttpClient.SendAsync(requestMessage2);

        // Assert
        Assert.Equal(HttpStatusCode.Accepted, response1.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, response2.StatusCode);


        await _helpers.AssertLedgerAfterBatchDeductionAsync(true, true, true, false,
            TestConstants.TargetVideoId, TestFixture.TestingDateTimeOffset);
        await _helpers.AssertWalletAsync(TestConstants.AiTitleEnqueuedCost + TestConstants.AiDescriptionEnqueuedCost + TestConstants.AiTagsEnqueuedCost);
        using var verificationScope = fixture.ApiServices.CreateScope();
        var verificationContext = verificationScope.ServiceProvider.GetRequiredService<TubesterDb>();

        var historyRecords = await verificationContext.SubscriptionHistories
            .AsNoTracking()
            .Where(history => history.UserId == TestConstants.UserId)
            .ToListAsync();

        Assert.Single(historyRecords);

        // Verify only one AiTemplateJob was enqueued with correct parameters
        var enqueuedJobs = fixture.CapturingJobClient.GetEnqueued<AiTemplateJob>();
        Assert.Single(enqueuedJobs);

        var capturedJob = enqueuedJobs[0];
        Assert.Equal(nameof(AiTemplateJob.Run), capturedJob.Job.Method.Name);

        var enqueuedRequest = Assert.IsType<AiVideoDetailsRequest>(capturedJob.Job.Args.SingleOrDefault(a => a is AiVideoDetailsRequest));
        Assert.Equal(TestConstants.ChannelId, enqueuedRequest.ChannelId);
        Assert.Equal(TestConstants.UploadsPlaylistId, enqueuedRequest.UploadPlaylistId);
        Assert.Equal(request.TargetVideoId, enqueuedRequest.TargetVideoId);
        Assert.Equal(request.PromptEnrichment, enqueuedRequest.PromptEnrichment);
        Assert.Equal(request.GenerateTitle, enqueuedRequest.GenerateTitle);
        Assert.Equal(request.GenerateDescription, enqueuedRequest.GenerateDescription);
        Assert.Equal(request.GenerateTags, enqueuedRequest.GenerateTags);

        var videoInDatabase = await verificationContext.Videos.FindAsync(testData.Video!.VideoId);

        Assert.NotNull(videoInDatabase);
        Assert.True(
            videoInDatabase.IsAiTitleInProgress ||
            videoInDatabase.IsAiDescriptionInProgress ||
            videoInDatabase.IsAiTagsInProgress);

        await _helpers.AssertUserEventAsync(CreditActionType.AiTemplateEnqueued, TestConstants.UserId, testData.Video!.VideoId);
    }
    [Fact]
    public async Task AiTemplate_WhenSubscriptionExpiredAndWalletMissing_RenewsAndCreatesWallet()
    {
        // Arrange
        await fixture.CleanStateAsync();

        var testData = await _helpers.SeedTestDataAsync(new TestDataOptions
        {
            CreateSubscription = false
        });

        var expiredPeriodStart = TestFixture.TestingDateTimeOffset.AddMonths(-3);
        var expiredPeriodEnd = TestFixture.TestingDateTimeOffset.AddMonths(-2);

        using var serviceScope = fixture.ApiServices.CreateScope();
        var databaseContext = serviceScope.ServiceProvider.GetRequiredService<TubesterDb>();

        databaseContext.Subscriptions.Add(new Subscription
        {
            UserId = TestConstants.UserId,
            PlanId = testData.Plan!.Id,
            PeriodStartUtc = expiredPeriodStart,
            PeriodEndUtc = expiredPeriodEnd,
            Status = SubscriptionStatus.Active
        });

        await databaseContext.SaveChangesAsync();

        var request = new AiVideoTemplateRequest
        {
            TargetVideoId = TestConstants.TargetVideoId,
            PromptEnrichment = "Generate better metadata",
            ExpectedCreditCost = TestConstants.AiTitleEnqueuedCost + TestConstants.AiDescriptionEnqueuedCost + TestConstants.AiTagsEnqueuedCost
        };

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, "/api/videos/ai-template")
        {
            Content = TestHelpers.CreateJsonContent(request)
        };
        requestMessage.Headers.Add("OperationId", OperationId);

        // Act
        var response = await fixture.HttpClient.SendAsync(requestMessage);

        // Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        using var verificationScope = fixture.ApiServices.CreateScope();
        var verificationContext = verificationScope.ServiceProvider.GetRequiredService<TubesterDb>();

        var wallet = await verificationContext.Wallets
            .AsNoTracking()
            .SingleOrDefaultAsync(wallet => wallet.UserId == TestConstants.UserId);

        Assert.NotNull(wallet);
        Assert.Equal(TestFixture.TestingDateTimeOffset, wallet.PeriodStartUtc);
        Assert.Equal(TestFixture.TestingDateTimeOffset.AddMonths(1), wallet.PeriodEndUtc);
        Assert.Equal(testData.Plan!.MonthlyCredits - TotalCost, wallet.Balance);

        var historyRecord = await verificationContext.SubscriptionHistories
            .AsNoTracking()
            .SingleOrDefaultAsync(history => history.UserId == TestConstants.UserId);

        Assert.NotNull(historyRecord);
        Assert.Equal(expiredPeriodStart, historyRecord.PeriodStartUtc);
        Assert.Equal(expiredPeriodEnd, historyRecord.PeriodEndUtc);
    }
    [Fact]
    public async Task AiTemplate_WhenSubscriptionActiveButWalletExpired_RefreshesWalletWithoutRenewingSubscription()
    {
        // Arrange
        await fixture.CleanStateAsync();

        var testData = await _helpers.SeedTestDataAsync(new TestDataOptions
        {
            CreateSubscription = false
        });

        var activePeriodStart = TestFixture.TestingDateTimeOffset;
        var activePeriodEnd = TestFixture.TestingDateTimeOffset.AddMonths(1);

        var expiredWalletStart = TestFixture.TestingDateTimeOffset.AddMonths(-1);
        var expiredWalletEnd = TestFixture.TestingDateTimeOffset;

        using var serviceScope = fixture.ApiServices.CreateScope();
        var databaseContext = serviceScope.ServiceProvider.GetRequiredService<TubesterDb>();

        databaseContext.Subscriptions.Add(new Subscription
        {
            UserId = TestConstants.UserId,
            PlanId = testData.Plan!.Id,
            PeriodStartUtc = activePeriodStart,
            PeriodEndUtc = activePeriodEnd,
            Status = SubscriptionStatus.Active
        });

        databaseContext.Wallets.Add(new Wallet
        {
            UserId = TestConstants.UserId,
            Balance = 1,
            PeriodStartUtc = expiredWalletStart,
            PeriodEndUtc = expiredWalletEnd,
            UpdatedAtUtc = expiredWalletEnd
        });

        await databaseContext.SaveChangesAsync();

        var request = new AiVideoTemplateRequest
        {
            TargetVideoId = TestConstants.TargetVideoId,
            PromptEnrichment = "Generate better metadata",
            ExpectedCreditCost = TestConstants.AiTitleEnqueuedCost + TestConstants.AiDescriptionEnqueuedCost + TestConstants.AiTagsEnqueuedCost
        };

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, "/api/videos/ai-template")
        {
            Content = TestHelpers.CreateJsonContent(request)
        };
        requestMessage.Headers.Add("OperationId", OperationId);

        // Act
        var response = await fixture.HttpClient.SendAsync(requestMessage);

        // Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        using var verificationScope = fixture.ApiServices.CreateScope();
        var verificationContext = verificationScope.ServiceProvider.GetRequiredService<TubesterDb>();

        var subscription = await verificationContext.Subscriptions
            .AsNoTracking()
            .SingleAsync(subscription => subscription.UserId == TestConstants.UserId);

        Assert.Equal(activePeriodStart, subscription.PeriodStartUtc);
        Assert.Equal(activePeriodEnd, subscription.PeriodEndUtc);

        var wallet = await verificationContext.Wallets
            .AsNoTracking()
            .SingleAsync(wallet => wallet.UserId == TestConstants.UserId);

        Assert.Equal(activePeriodStart, wallet.PeriodStartUtc);
        Assert.Equal(activePeriodEnd, wallet.PeriodEndUtc);
        Assert.Equal(testData.Plan!.MonthlyCredits - TotalCost, wallet.Balance);

        Assert.Empty(await verificationContext.SubscriptionHistories
            .AsNoTracking()
            .Where(history => history.UserId == TestConstants.UserId)
            .ToListAsync());
    }
    [Fact]
    public async Task AiTemplate_WhenSubscriptionAndWalletCurrent_DoesNotGrantPeriodCreditsAgain()
    {
        // Arrange
        await fixture.CleanStateAsync();

        var testData = await _helpers.SeedTestDataAsync(new TestDataOptions
        {
            CreateSubscription = false
        });

        var periodStart = TestFixture.TestingDateTimeOffset;
        var periodEnd = TestFixture.TestingDateTimeOffset.AddMonths(1);
        var startingBalance = testData.Plan!.MonthlyCredits;

        using var serviceScope = fixture.ApiServices.CreateScope();
        var databaseContext = serviceScope.ServiceProvider.GetRequiredService<TubesterDb>();

        databaseContext.Subscriptions.Add(new Subscription
        {
            UserId = TestConstants.UserId,
            PlanId = testData.Plan.Id,
            PeriodStartUtc = periodStart,
            PeriodEndUtc = periodEnd,
            Status = SubscriptionStatus.Active
        });

        databaseContext.Wallets.Add(new Wallet
        {
            UserId = TestConstants.UserId,
            Balance = startingBalance,
            PeriodStartUtc = periodStart,
            PeriodEndUtc = periodEnd,
            UpdatedAtUtc = periodStart
        });

        await databaseContext.SaveChangesAsync();

        var request = new AiVideoTemplateRequest
        {
            TargetVideoId = TestConstants.TargetVideoId,
            PromptEnrichment = "Generate better metadata",
            ExpectedCreditCost = TestConstants.AiTitleEnqueuedCost + TestConstants.AiDescriptionEnqueuedCost + TestConstants.AiTagsEnqueuedCost
        };

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, "/api/videos/ai-template")
        {
            Content = TestHelpers.CreateJsonContent(request)
        };
        requestMessage.Headers.Add("OperationId", OperationId);

        // Act
        var response = await fixture.HttpClient.SendAsync(requestMessage);

        // Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        using var verificationScope = fixture.ApiServices.CreateScope();
        var verificationContext = verificationScope.ServiceProvider.GetRequiredService<TubesterDb>();

        var wallet = await verificationContext.Wallets
            .AsNoTracking()
            .SingleAsync(wallet => wallet.UserId == TestConstants.UserId);

        Assert.Equal(startingBalance - TotalCost, wallet.Balance);

        var periodGrantEntries = await verificationContext.LedgerEntries
            .AsNoTracking()
            .Where(entry => entry.UserId == TestConstants.UserId
                            && entry.ActionType == "PeriodGrant")
            .ToListAsync();

        Assert.Empty(periodGrantEntries);

        Assert.Empty(await verificationContext.SubscriptionHistories
            .AsNoTracking()
            .Where(history => history.UserId == TestConstants.UserId)
            .ToListAsync());
    }
    [Fact]
    public async Task AiTemplate_WhenUserHasNoSubscription_DoesNotGrantOrSpendCredits()
    {
        // Arrange
        await fixture.CleanStateAsync();

        await _helpers.SeedTestDataAsync(new TestDataOptions
        {
            CreateSubscription = false
        });

        var request = new AiVideoTemplateRequest
        {
            TargetVideoId = TestConstants.TargetVideoId,
            PromptEnrichment = "Generate better metadata"
        };

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, "/api/videos/ai-template")
        {
            Content = TestHelpers.CreateJsonContent(request)
        };
        requestMessage.Headers.Add("OperationId", OperationId);

        // Act
        var response = await fixture.HttpClient.SendAsync(requestMessage);

        // Assert
        Assert.Equal(HttpStatusCode.PaymentRequired, response.StatusCode);

        using var verificationScope = fixture.ApiServices.CreateScope();
        var verificationContext = verificationScope.ServiceProvider.GetRequiredService<TubesterDb>();

        Assert.Empty(await verificationContext.Subscriptions
            .AsNoTracking()
            .Where(subscription => subscription.UserId == TestConstants.UserId)
            .ToListAsync());

        Assert.Empty(await verificationContext.Wallets
            .AsNoTracking()
            .Where(wallet => wallet.UserId == TestConstants.UserId)
            .ToListAsync());

        Assert.Empty(await verificationContext.LedgerEntries
            .AsNoTracking()
            .Where(entry => entry.UserId == TestConstants.UserId)
            .ToListAsync());

        Assert.Empty(await verificationContext.SubscriptionHistories
            .AsNoTracking()
            .Where(history => history.UserId == TestConstants.UserId)
            .ToListAsync());
    }
}
