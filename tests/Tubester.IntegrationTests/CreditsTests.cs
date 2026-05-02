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
    private readonly TestHelpers _helpers = new(fixture);

    private const string OperationId = "credits-idempotency-operation";
    
    [Fact]
    public async Task AiTemplateEnqueue_WithInsufficientCredits_ReturnsForbidden_AndDoesNotPersistWalletOrLedger()
    {
        await fixture.ResetDbAsync();
        const int credits = 0;
        await _helpers.SeedVideoTestDataAsync(new TestDataOptions
        {
            MonthlyCredits = credits
        });
        
        var request = new AiVideoTemplateRequest
        {
            TargetVideoId = TestConstants.TargetVideoId,
            PromptEnrichment = "Generate better metadata for credits test"
        };

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, "/api/videos/ai-template")
        {
            Content = TestHelpers.CreateJsonContent(request)
        };
        requestMessage.Headers.Add("OperationId", OperationId);

        var response = await fixture.HttpClient.SendAsync(requestMessage);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var responseBody = await response.Content.ReadAsStringAsync();
        Assert.Contains("Insufficient credits to enqueue AI templating.", responseBody);
        await VerifyNoDeductionsAsync(credits);
    }

    [Fact]
    public async Task VideoDetailsSubmit_WithSufficientCredits_DeductsCreditsAndAppendsLedgerEntry()
    {
        await fixture.ResetDbAsync();
        await _helpers.SeedVideoTestDataAsync();

        const string newTitle = "Updated Title";
        const string newDescription = "Updated Description";
        var newTags = new[] { "new-tag" };

        fixture.ApiFactory.MockYouTubeIntegration
            .Setup(youTubeIntegration => youTubeIntegration.UpdateVideoAsync(
                TestConstants.TargetVideoId,
                newTitle,
                newDescription,
                It.Is<IReadOnlyList<string>>(tags => tags.SequenceEqual(newTags)),
                TestConstants.TargetVideoCategoryId,
                TestConstants.DefaultLanguage,
                TestConstants.DefaultAudioLanguage,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var request = new UpdateVideoMetadataRequest(
            TestConstants.TargetVideoId,
            newTitle,
            newDescription,
            newTags,
            null);

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, "/api/videos/update")
        {
            Content = TestHelpers.CreateJsonContent(request)
        };
        requestMessage.Headers.Add("OperationId", OperationId);

        var response = await fixture.HttpClient.SendAsync(requestMessage);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await _helpers.VerifyLedgerAndWalletAfterDeductionAsync(nameof(CreditActionType.AiTemplateSubmitted),
            TestConstants.VideoDetailsSubmitActionCost,TestConstants.TargetVideoId);
    }

    [Fact]
    public async Task VideoDetailsSubmit_WithInsufficientCredits_ReturnsForbidden_AndDoesNotDeductCredits()
    {
        await fixture.ResetDbAsync();
        await _helpers.SeedVideoTestDataAsync(new TestDataOptions
        {
            MonthlyCredits = 1
        });
        
        var request = new UpdateVideoMetadataRequest(
            TestConstants.TargetVideoId,
            "Updated Title",
            "Updated Description",
            ["new-tag"],
            null);

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, "/api/videos/update")
        {
            Content = TestHelpers.CreateJsonContent(request)
        };
        requestMessage.Headers.Add("OperationId", OperationId);

        var response = await fixture.HttpClient.SendAsync(requestMessage);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var responseBody = await response.Content.ReadAsStringAsync();
        Assert.Contains("Insufficient credits to submit AI template changes.", responseBody);

        await VerifyNoDeductionsAsync(1);
    }

    [Fact]
    public async Task ReplyPostedToYouTube_WithSufficientCredits_DeductsCreditsAndAppendsLedgerEntry()
    {
        await fixture.ResetDbAsync();
        fixture.ApiFactory.MockYouTubeIntegration.Reset();
        await _helpers.SeedVideoTestDataAsync();
        
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

        await _helpers.VerifyLedgerAndWalletAfterDeductionAsync(nameof(CreditActionType.ReplyPostedToYouTube),
            TestConstants.ReplyPostedActionCost, reply.CommentId);
    }

    [Fact]
    public async Task ReplyPostedToYouTube_WithInsufficientCredits_FailsWithInsufficientCreditsMessage()
    {
        await fixture.ResetDbAsync();
        fixture.ApiFactory.MockYouTubeIntegration.Reset();
        await _helpers.SeedVideoTestDataAsync(new TestDataOptions { MonthlyCredits = 0});

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

        var responseContent = await response.Content.ReadAsStringAsync();
        var result =
            JsonSerializer.Deserialize<BatchDecisionResultDto>(responseContent, TestHelpers.SerializerOptions);

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
        await fixture.ResetDbAsync();
        await _helpers.SeedVideoTestDataAsync(new TestDataOptions
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
            PromptEnrichment = "Generate better metadata"
        };

        var requestJson = JsonSerializer.Serialize(request, TestHelpers.SerializerOptions);
        var requestContent = new StringContent(requestJson, Encoding.UTF8, "application/json");

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, "/api/videos/ai-template")
        {
            Content = requestContent
        };
        requestMessage.Headers.Add("OperationId", OperationId);

        var response = await fixture.HttpClient.SendAsync(requestMessage);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await _helpers.VerifyLedgerAndWalletAfterDeductionAsync(nameof(CreditActionType.AiTemplateEnqueued), TestConstants.AiTemplateCost, TestConstants.TargetVideoId);
    }

    [Fact]
    public async Task TrySpend_WhenWalletPeriodMatchesSubscription_DoesNotGrantNewCredits()
    {
        await fixture.ResetDbAsync();
        await _helpers.SeedVideoTestDataAsync(new TestDataOptions
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
            MonthlyCredits = 10,
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
            PromptEnrichment = "Generate better metadata"
        };

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, "/api/videos/ai-template")
        {
            Content = TestHelpers.CreateJsonContent(request)
        };
        requestMessage.Headers.Add("OperationId", OperationId);

        var response = await fixture.HttpClient.SendAsync(requestMessage);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await _helpers.VerifyLedgerAndWalletAfterDeductionAsync(nameof(CreditActionType.AiTemplateEnqueued),
            TestConstants.AiTemplateCost, TestConstants.TargetVideoId, oldGrantAt);
    }

    [Fact]
    public async Task AiReplyGenerated_WithSufficientCredits_DeductsCreditsAndAppendsLedgerEntry()
    {
        await fixture.ResetDbAsync();
        fixture.WorkerFactory.MockBackgroundYoutubeIntegration.Reset();
        fixture.WorkerFactory.MockAiClient.Invocations.Clear();
        
        var video = TestHelpers.GetTargetVideo();
        await _helpers.SeedVideoTestDataAsync(new TestDataOptions
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

        fixture.WorkerFactory.MockAiClient
            .Setup(client => client.SuggestReplyAsync(
                video.Title!,
                video.Tags,
                commentThread.Text,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("Thanks for watching! I'll cover that in a future video.");
        
        using (var jobScope = fixture.WorkerServices.CreateScope())
        {
            var commentScanJob = jobScope.ServiceProvider.GetRequiredService<CommentScanJob>();
            await commentScanJob.Run(TestConstants.ChannelId, new Hangfire.JobCancellationToken(false));
        }
        
        await _helpers.VerifyLedgerAndWalletAfterDeductionAsync(nameof(CreditActionType.AiReplyGenerated), 
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
    public async Task AiReplyGenerated_WhenAiClientFails_RefundsCredits()
    {
        await fixture.ResetDbAsync();
        fixture.WorkerFactory.MockBackgroundYoutubeIntegration.Invocations.Clear();
        fixture.WorkerFactory.MockAiClient.Invocations.Clear();
        var video = TestHelpers.GetTargetVideo();
        await _helpers.SeedVideoTestDataAsync(new TestDataOptions
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

        fixture.WorkerFactory.MockAiClient
            .Setup(client => client.SuggestReplyAsync(
                video.Title!,
                video.Tags,
                commentThread.Text,
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
        await fixture.ResetDbAsync();
        fixture.WorkerFactory.MockBackgroundYoutubeIntegration.Reset();
        fixture.WorkerFactory.MockAiClient.Invocations.Clear();

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
}
