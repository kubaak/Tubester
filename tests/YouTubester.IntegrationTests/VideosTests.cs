using System.Net;
using System.Text;
using System.Text.Json;
using AutoFixture;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;
using YouTubester.Abstractions.Analytics;
using YouTubester.Abstractions.Credits;
using YouTubester.Abstractions.Users;
using YouTubester.Application.Contracts;
using YouTubester.Application.Contracts.Videos;
using YouTubester.Application.Jobs;
using YouTubester.Domain;
using YouTubester.IntegrationTests.TestHost;
using YouTubester.Persistence;
using YouTubester.Persistence.Credits;
using StringContent = System.Net.Http.StringContent;

namespace YouTubester.IntegrationTests;

[Collection(nameof(TestCollection))]
public class VideosTests(TestFixture fixture)
{
    private readonly JsonSerializerOptions _serializerOptions =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private const string OperationId = "credits-idempotency-operation";

    [Fact]
    public async Task GetVideos_EmptyDb_ReturnsEmptyList()
    {
        // Arrange
        await fixture.ResetDbAsync();

        // Act
        var response = await fixture.HttpClient.GetAsync("/api/videos?pageSize=5");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PagedResult<VideoListItemDto>>(content, _serializerOptions);

        Assert.NotNull(result);
        Assert.Empty(result.Items);
        Assert.Null(result.NextPageToken);
    }

    [Fact]
    public async Task GetVideos_WithInvalidVisibility_ReturnsBadRequest()
    {
        // Arrange
        await fixture.ResetDbAsync();

        // Act
        var response = await fixture.HttpClient.GetAsync("/api/videos?visibility=InvalidValue");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetVideos_WithValidVisibilityFilter_ReturnsOk()
    {
        // Arrange
        await fixture.ResetDbAsync();

        // Act
        var response = await fixture.HttpClient.GetAsync("/api/videos?visibility=Public&visibility=Unlisted");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PagedResult<VideoListItemDto>>(content, _serializerOptions);

        Assert.NotNull(result);
        Assert.Empty(result.Items);
        Assert.Null(result.NextPageToken);
    }

    [Fact]
    public async Task GetVideos_CaseInsensitiveVisibility_ReturnsOk()
    {
        // Arrange
        await fixture.ResetDbAsync();

        // Act
        var response = await fixture.HttpClient.GetAsync("/api/videos?visibility=public&visibility=UNLISTED");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PagedResult<VideoListItemDto>>(content, _serializerOptions);

        Assert.NotNull(result);
        Assert.Empty(result.Items);
        Assert.Null(result.NextPageToken);
    }

    [Fact]
    public async Task GetVideo_ExistingVideo_ReturnsOkWithDetails()
    {
        // Arrange
        await fixture.ResetDbAsync();

        const string uploadPlaylistId = "PLVideoDetailsUploads";
        const string channelId = "video-details-channel";

        fixture.ApiFactory.MockCurrentChannelContext
            .Setup(channelContext => channelContext.GetRequiredChannelId())
            .Returns(channelId);

        const string videoId = "videoDetail1";
        const string title = "Video Details Title";
        const string description = "Video Details Description";
        var tags = new[] { "tag-one", "tag-two" };

        var video = Video.Create(
            uploadPlaylistId,
            videoId,
            title,
            description,
            TestFixture.TestingDateTimeOffset,
            TimeSpan.FromMinutes(2),
            VideoVisibility.Public,
            tags,
            "22",
            "en",
            "en",
            null,
            null,
            TestFixture.TestingDateTimeOffset,
            "etag-video-details"
        );

        using (var serviceScope = fixture.ApiServices.CreateScope())
        {
            var databaseContext = serviceScope.ServiceProvider.GetRequiredService<YouTubesterDb>();
            var user = User.Create(
                MockAuthenticationExtensions.TestSub,
                MockAuthenticationExtensions.TestEmail,
                MockAuthenticationExtensions.TestName,
                MockAuthenticationExtensions.TestPicture,
                TestFixture.TestingDateTimeOffset);
            var channel = Channel.Create(channelId, MockAuthenticationExtensions.TestSub, "Video Details Channel",
                uploadPlaylistId, TestFixture.TestingDateTimeOffset);

            databaseContext.Users.Add(user);
            databaseContext.Channels.Add(channel);
            databaseContext.Videos.Add(video);
            await databaseContext.SaveChangesAsync();
        }

        // Act
        var response = await fixture.HttpClient.GetAsync($"/api/videos/{videoId}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var videoDetails = JsonSerializer.Deserialize<VideoDetailsDto>(content, _serializerOptions);

        Assert.NotNull(videoDetails);
        Assert.Equal(title, videoDetails.Title);
        Assert.Equal(description, videoDetails.Description);
        Assert.NotNull(videoDetails.Tags);
        Assert.Equal(tags, videoDetails.Tags);
        Assert.False(videoDetails.IsAiTemplateInProgress);
    }

    [Fact]
    public async Task GetVideo_VideoDoesNotExist_ReturnsNotFound()
    {
        // Arrange
        await fixture.ResetDbAsync();

        const string uploadPlaylistId = "PLVideoDetailsUploads";
        const string channelId = "video-details-channel";

        fixture.ApiFactory.MockCurrentChannelContext
            .Setup(channelContext => channelContext.GetRequiredChannelId())
            .Returns(channelId);

        using (var serviceScope = fixture.ApiServices.CreateScope())
        {
            var databaseContext = serviceScope.ServiceProvider.GetRequiredService<YouTubesterDb>();
            var user = User.Create(
                MockAuthenticationExtensions.TestSub,
                MockAuthenticationExtensions.TestEmail,
                MockAuthenticationExtensions.TestName,
                MockAuthenticationExtensions.TestPicture,
                TestFixture.TestingDateTimeOffset);
            var channel = Channel.Create(channelId, MockAuthenticationExtensions.TestSub, "Video Details Channel",
                uploadPlaylistId, TestFixture.TestingDateTimeOffset);

            databaseContext.Users.Add(user);
            databaseContext.Channels.Add(channel);
            await databaseContext.SaveChangesAsync();
        }

        // Act
        var response = await fixture.HttpClient.GetAsync("/api/videos/does-not-exist");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateVideo_ValidRequest_UpdatesYoutubeAndReturnsUpdatedDetails()
    {
        // Arrange
        await fixture.ResetDbAsync();

        const string uploadPlaylistId = "PLVideoUpdateUploads";
        const string channelId = "video-update-channel";

        fixture.ApiFactory.MockCurrentChannelContext
            .Setup(channelContext => channelContext.GetRequiredChannelId())
            .Returns(channelId);

        const string originalVideoId = "videoUpdate1";
        var originalVideo = Video.Create(
            uploadPlaylistId,
            originalVideoId,
            "Original Title",
            "Original Description",
            TestFixture.TestingDateTimeOffset,
            TimeSpan.FromMinutes(2),
            VideoVisibility.Public,
            ["original"],
            "22",
            "en",
            "en",
            new GeoLocation(37.7749, -122.4194),
            "San Francisco, CA",
            TestFixture.TestingDateTimeOffset,
            "etag-video-update"
        );

        using (var serviceScope = fixture.ApiServices.CreateScope())
        {
            var databaseContext = serviceScope.ServiceProvider.GetRequiredService<YouTubesterDb>();
            var user = User.Create(
                MockAuthenticationExtensions.TestSub,
                MockAuthenticationExtensions.TestEmail,
                MockAuthenticationExtensions.TestName,
                MockAuthenticationExtensions.TestPicture,
                TestFixture.TestingDateTimeOffset);
            var channel = Channel.Create(channelId, MockAuthenticationExtensions.TestSub, "Video Update Channel",
                uploadPlaylistId, TestFixture.TestingDateTimeOffset);

            databaseContext.Users.Add(user);
            await databaseContext.SaveChangesAsync();
            databaseContext.Channels.Add(channel);
            databaseContext.Videos.Add(originalVideo);

            var aiTemplateSubmittedCost = new ActionCost
            {
                ActionType = CreditActionType.AiTemplateSubmitted.ToString(),
                Cost = 0,
                IsEnabled = true,
                UpdatedAtUtc = TestFixture.TestingDateTimeOffset,
                Notes = "Integration test cost for AiTemplateSubmitted."
            };

            await databaseContext.ActionCosts.AddAsync(aiTemplateSubmittedCost, CancellationToken.None);

            var plan = new Plan
            {
                Code = "FreePlan",
                Name = "Free Plan",
                MonthlyCredits = 5,
                IsActive = true,
                CreatedAtUtc = TestFixture.TestingDateTimeOffset,
                UpdatedAtUtc = TestFixture.TestingDateTimeOffset
            };

            await databaseContext.Plans.AddAsync(plan, CancellationToken.None);
            await databaseContext.SaveChangesAsync(CancellationToken.None);
            var userSubscription = new Subscription
            {
                UserId = user.Id,
                PlanId = plan.Id,
                PeriodStartUtc = TestFixture.TestingDateTimeOffset,
                PeriodEndUtc = TestFixture.TestingDateTimeOffset.AddMonths(1),
                Status = SubscriptionStatus.Active
            };
            await databaseContext.Subscriptions.AddAsync(userSubscription, CancellationToken.None);
            await databaseContext.SaveChangesAsync();
        }

        const string newTitle = "Updated Title";
        const string newDescription = "Updated Description";
        var newTags = new[] { "tag-one", "tag-two" };

        fixture.ApiFactory.MockYouTubeIntegration
            .Setup(youTubeIntegration => youTubeIntegration.UpdateVideoAsync(
                originalVideoId,
                newTitle,
                newDescription,
                It.Is<IReadOnlyList<string>>(tags => tags.SequenceEqual(newTags)),
                originalVideo.CategoryId,
                originalVideo.DefaultLanguage,
                originalVideo.DefaultAudioLanguage,
                It.IsAny<(double lat, double lng)?>(),
                originalVideo.LocationDescription,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var request = new UpdateVideoMetadataRequest(
            originalVideoId,
            newTitle,
            newDescription,
            newTags
        );

        var requestJson = JsonSerializer.Serialize(request, _serializerOptions);
        var requestHttpContent = new StringContent(requestJson, Encoding.UTF8, "application/json");

        // Act
        var response = await fixture.HttpClient.PostAsync("/api/videos/update", requestHttpContent);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var responseJson = await response.Content.ReadAsStringAsync();
        var videoDetails = JsonSerializer.Deserialize<VideoDetailsDto>(responseJson, _serializerOptions);

        Assert.NotNull(videoDetails);
        Assert.Equal(newTitle, videoDetails.Title);
        Assert.Equal(newDescription, videoDetails.Description);
        Assert.Equal(newTags, videoDetails.Tags);
        Assert.False(videoDetails.IsAiTemplateInProgress);

        fixture.ApiFactory.MockYouTubeIntegration.Verify(youTubeIntegration =>
                youTubeIntegration.UpdateVideoAsync(
                    originalVideoId,
                    newTitle,
                    newDescription,
                    It.Is<IReadOnlyList<string>>(tags => tags.SequenceEqual(newTags)),
                    originalVideo.CategoryId,
                    originalVideo.DefaultLanguage,
                    originalVideo.DefaultAudioLanguage,
                    It.IsAny<(double lat, double lng)?>(),
                    originalVideo.LocationDescription,
                    It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify AiTemplateSubmitted analytics event is logged
        using (var verifyScope = fixture.ApiServices.CreateScope())
        {
            var dbContext = verifyScope.ServiceProvider.GetRequiredService<YouTubesterDb>();
            var events = await dbContext.UserEvents
                .Where(e => e.EventType == CreditActionType.AiTemplateSubmitted.ToString())
                .ToListAsync();

            Assert.Single(events);
            var userEvent = events[0];
            Assert.Equal(MockAuthenticationExtensions.TestSub, userEvent.UserId);
            Assert.Equal(originalVideoId, userEvent.VideoId);
            Assert.Null(userEvent.CommentId);
            Assert.False(string.IsNullOrWhiteSpace(userEvent.MetadataJson));
        }
    }

    [Fact]
    public async Task CopyTemplate_ValidRequest_CallsYoutubeService_AndLogsAnalytics()
    {
        const string channelId = "Channel-XYZ";
        const string uploadPlaylistId = "ULTestPlaylist123";
        const string userId = MockAuthenticationExtensions.TestSub;
        // Arrange
        await fixture.ResetDbAsync();
        fixture.ApiFactory.MockCurrentChannelContext.Setup(x => x.GetRequiredChannelId())
            .Returns(channelId);

        var sourceVideo = GetSourceVideo(uploadPlaylistId);
        var targetVideo = GetTargetVideo(uploadPlaylistId);

        using (var scope = fixture.ApiServices.CreateScope())
        {
            var databaseContext = scope.ServiceProvider.GetRequiredService<YouTubesterDb>();
            var user = User.Create(
                userId,
                MockAuthenticationExtensions.TestEmail,
                MockAuthenticationExtensions.TestName,
                MockAuthenticationExtensions.TestPicture,
                TestFixture.TestingDateTimeOffset);
            await databaseContext.Users.AddAsync(user, CancellationToken.None);
            await databaseContext.Channels.AddAsync(Channel.Create(channelId, userId, "Channel A",
                uploadPlaylistId, TestFixture.TestingDateTimeOffset), CancellationToken.None);
            databaseContext.Videos.AddRange(sourceVideo, targetVideo);

            var plan = new Plan
            {
                Code = "CopyTemplateTestPlan",
                Name = "Copy Template Test Plan",
                MonthlyCredits = 10,
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
                Cost = 1,
                IsEnabled = true,
                UpdatedAtUtc = TestFixture.TestingDateTimeOffset,
                Notes = "Integration test cost for CopyTemplateExecuted."
            };

            await databaseContext.ActionCosts.AddAsync(copyTemplateCost, CancellationToken.None);
            await databaseContext.SaveChangesAsync(CancellationToken.None);
        }

        fixture.ApiFactory.MockYouTubeIntegration.Setup(x =>
                x.UpdateVideoAsync(targetVideo.VideoId, sourceVideo.Title!, sourceVideo.Description!,
                    sourceVideo.Tags, targetVideo.CategoryId, sourceVideo.DefaultLanguage,
                    sourceVideo.DefaultAudioLanguage,
                    It.IsAny<(double lat, double lng)?>(),
                    targetVideo.LocationDescription, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var request = new CopyVideoTemplateRequest(
            sourceVideo.VideoId,
            targetVideo.VideoId,
            OperationId,
            true,
            false,
            true,
            false,
            true
        );

        var json = JsonSerializer.Serialize(request, _serializerOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await fixture.HttpClient.PostAsync("/api/videos/copy-template", content);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var responseContent = await response.Content.ReadAsStringAsync();
        Assert.False(string.IsNullOrWhiteSpace(responseContent));

        fixture.ApiFactory.MockYouTubeIntegration.Verify(x =>
                x.UpdateVideoAsync(
                    targetVideo.VideoId,
                    sourceVideo.Title!,
                    sourceVideo.Description!,
                    It.Is<IReadOnlyList<string>>(tags =>
                        tags.SequenceEqual(sourceVideo.Tags)),
                    targetVideo.CategoryId,
                    sourceVideo.DefaultLanguage,
                    sourceVideo.DefaultAudioLanguage,
                    It.IsAny<(double lat, double lng)?>(),
                    targetVideo.LocationDescription,
                    It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify CopyTemplateExecuted analytics event is logged
        using (var verifyScope = fixture.ApiServices.CreateScope())
        {
            var dbContext = verifyScope.ServiceProvider.GetRequiredService<YouTubesterDb>();
            var events = await dbContext.UserEvents
                .Where(e => e.EventType == CreditActionType.CopyTemplateExecuted.ToString())
                .ToListAsync();

            Assert.Single(events);
            var userEvent = events[0];
            Assert.Equal(userId, userEvent.UserId);
            Assert.Equal(targetVideo.VideoId, userEvent.VideoId);
            Assert.Null(userEvent.CommentId);
            Assert.False(string.IsNullOrWhiteSpace(userEvent.MetadataJson));
        }
    }

    [Fact]
    public async Task CopyTemplate_EmptySourceVideoId_ReturnsBadRequest()
    {
        // Arrange
        await fixture.ResetDbAsync();

        var request = new CopyVideoTemplateRequest(
            "",
            "targetVideoId456",
            OperationId
        );

        var json = JsonSerializer.Serialize(request, _serializerOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await fixture.HttpClient.PostAsync("/api/videos/copy-template", content);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var responseContent = await response.Content.ReadAsStringAsync();
        Assert.Contains("SourceVideoId is required", responseContent);
    }

    [Fact]
    public async Task CopyTemplate_EmptyTargetVideoId_ReturnsBadRequest()
    {
        // Arrange
        await fixture.ResetDbAsync();

        var request = new CopyVideoTemplateRequest(
            "sourceVideoId123",
            "",
            OperationId
        );

        var json = JsonSerializer.Serialize(request, _serializerOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await fixture.HttpClient.PostAsync("/api/videos/copy-template", content);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var responseContent = await response.Content.ReadAsStringAsync();
        Assert.Contains("TargetVideoId is required", responseContent);
    }

    [Fact]
    public async Task CopyTemplate_SameSourceAndTargetVideoId_ReturnsBadRequest()
    {
        // Arrange
        await fixture.ResetDbAsync();

        var request = new CopyVideoTemplateRequest(
            "sameVideoId123",
            "sameVideoId123",
            OperationId
        );

        var json = JsonSerializer.Serialize(request, _serializerOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await fixture.HttpClient.PostAsync("/api/videos/copy-template", content);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var responseContent = await response.Content.ReadAsStringAsync();
        Assert.Contains("SourceVideoId and TargetVideoId must be different", responseContent);
    }

    [Fact]
    public async Task AiTemplate_ValidRequest_EnqueuesAiTemplateJob_AndLogsAnalytics()
    {
        // Arrange
        await fixture.ResetDbAsync();

        const string channelId = "ai-template-channel";
        const string uploadPlaylistId = "ULTestPlaylist456";
        const string userId = MockAuthenticationExtensions.TestSub;

        fixture.ApiFactory.MockCurrentChannelContext
            .Setup(channelContext => channelContext.GetRequiredChannelId())
            .Returns(channelId);

        var targetVideo = GetTargetVideo(uploadPlaylistId);

        using (var serviceScope = fixture.ApiServices.CreateScope())
        {
            var databaseContext = serviceScope.ServiceProvider.GetRequiredService<YouTubesterDb>();
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
                "AI Template Channel",
                targetVideo.UploadsPlaylistId,
                TestFixture.TestingDateTimeOffset));

            databaseContext.Videos.Add(targetVideo);

            var plan = new Plan
            {
                Code = "AiTemplateEnqueueTestPlan",
                Name = "AI Template Enqueue Test Plan",
                MonthlyCredits = 10,
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
                Notes = "Integration test cost for AiTemplateEnqueued."
            };

            await databaseContext.ActionCosts.AddAsync(aiTemplateEnqueuedCost, CancellationToken.None);
            await databaseContext.SaveChangesAsync(CancellationToken.None);
        }

        var request = new AiVideoTemplateRequest(
            targetVideo.VideoId,
            "Generate a better title, description, and tags"
        );

        var serializedRequest = JsonSerializer.Serialize(request, _serializerOptions);
        var requestContent = new StringContent(serializedRequest, Encoding.UTF8, "application/json");

        // Act
        var videosEndpointResponse = await fixture.HttpClient.PostAsync("/api/videos/ai-template", requestContent);

        // Assert
        Assert.Equal(HttpStatusCode.OK, videosEndpointResponse.StatusCode);

        var videosEndpointResponseBody = await videosEndpointResponse.Content.ReadAsStringAsync();
        var enqueueResult =
            JsonSerializer.Deserialize<AiTemplateEnqueueResult>(videosEndpointResponseBody, _serializerOptions);

        Assert.NotNull(enqueueResult);
        Assert.False(string.IsNullOrWhiteSpace(enqueueResult.JobId));

        var capturedJobs = fixture.CapturingJobClient.GetEnqueued<AiTemplateJob>();
        Assert.Single(capturedJobs);
        Assert.Equal(enqueueResult.JobId, capturedJobs[0].JobId);

        Assert.Equal(nameof(AiTemplateJob.Run), capturedJobs[0].Job.Method.Name);
        Assert.Equal(channelId, capturedJobs[0].Job.Args[0]);

        var enqueuedRequest = Assert.IsType<AiVideoTemplateRequest>(capturedJobs[0].Job.Args[1]);
        Assert.Equal(request.TargetVideoId, enqueuedRequest.TargetVideoId);
        Assert.Equal(request.PromptEnrichment, enqueuedRequest.PromptEnrichment);
        Assert.Equal(request.GenerateTitle, enqueuedRequest.GenerateTitle);
        Assert.Equal(request.GenerateDescription, enqueuedRequest.GenerateDescription);
        Assert.Equal(request.GenerateTags, enqueuedRequest.GenerateTags);

        using (var serviceScope = fixture.ApiServices.CreateScope())
        {
            var databaseContext = serviceScope.ServiceProvider.GetRequiredService<YouTubesterDb>();
            var videoInDatabase = await databaseContext.Videos.FindAsync(targetVideo.VideoId);
            Assert.NotNull(videoInDatabase);
            Assert.True(videoInDatabase.IsAiTemplateInProgress);
        }

        // Verify AiTemplateEnqueued analytics event is logged
        using (var verifyScope = fixture.ApiServices.CreateScope())
        {
            var dbContext = verifyScope.ServiceProvider.GetRequiredService<YouTubesterDb>();
            var events = await dbContext.UserEvents
                .Where(e => e.EventType == CreditActionType.AiTemplateEnqueued.ToString())
                .ToListAsync();

            Assert.Single(events);
            var userEvent = events[0];
            Assert.Equal(userId, userEvent.UserId);
            Assert.Equal(targetVideo.VideoId, userEvent.VideoId);
            Assert.Null(userEvent.CommentId);
            Assert.False(string.IsNullOrWhiteSpace(userEvent.MetadataJson));
        }
    }

    [Fact]
    public async Task AiTemplate_EmptyTargetVideoId_ReturnsBadRequest()
    {
        // Arrange
        await fixture.ResetDbAsync();

        var request = new AiVideoTemplateRequest(
            "",
            "Generate metadata"
        );

        var serializedRequest = JsonSerializer.Serialize(request, _serializerOptions);
        var requestContent = new StringContent(serializedRequest, Encoding.UTF8, "application/json");

        // Act
        var videosEndpointResponse = await fixture.HttpClient.PostAsync("/api/videos/ai-template", requestContent);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, videosEndpointResponse.StatusCode);

        var responseContent = await videosEndpointResponse.Content.ReadAsStringAsync();
        Assert.Contains("TargetVideoId is required", responseContent);
    }

    [Fact]
    public async Task AiTemplate_EmptyPromptEnrichment_ReturnsBadRequest()
    {
        // Arrange
        await fixture.ResetDbAsync();

        var request = new AiVideoTemplateRequest(
            "targetVideoId123",
            ""
        );

        var serializedRequest = JsonSerializer.Serialize(request, _serializerOptions);
        var requestContent = new StringContent(serializedRequest, Encoding.UTF8, "application/json");

        // Act
        var videosEndpointResponse = await fixture.HttpClient.PostAsync("/api/videos/ai-template", requestContent);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, videosEndpointResponse.StatusCode);

        var responseContent = await videosEndpointResponse.Content.ReadAsStringAsync();
        Assert.Contains("PromptEnrichment is required", responseContent);
    }

    [Fact]
    public async Task AiTemplate_TargetVideoNotFound_ReturnsBadRequest()
    {
        // Arrange
        await fixture.ResetDbAsync();

        var channelId = "ai-template-channel";
        fixture.ApiFactory.MockCurrentChannelContext
            .Setup(channelContext => channelContext.GetRequiredChannelId())
            .Returns(channelId);

        var request = new AiVideoTemplateRequest(
            "missingTargetVideoId",
            "Generate metadata"
        );

        var serializedRequest = JsonSerializer.Serialize(request, _serializerOptions);
        var requestContent = new StringContent(serializedRequest, Encoding.UTF8, "application/json");

        // Act
        var videosEndpointResponse = await fixture.HttpClient.PostAsync("/api/videos/ai-template", requestContent);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, videosEndpointResponse.StatusCode);

        var responseContent = await videosEndpointResponse.Content.ReadAsStringAsync();
        Assert.Contains(
            $"Target video {request.TargetVideoId} not found for current channel or an AI template job is already in progress.",
            responseContent);
    }

    [Fact]
    public async Task GetVideos_WithVideosInDb_ReturnsFilteredAndPagedResults()
    {
        // Arrange
        await fixture.ResetDbAsync();

        const string testUploadsPlaylistId = "PLTestUploads123";

        const string testChannelId = "testChannelID123";
        var user = User.Create(
            MockAuthenticationExtensions.TestSub,
            MockAuthenticationExtensions.TestEmail,
            MockAuthenticationExtensions.TestName,
            MockAuthenticationExtensions.TestPicture,
            TestFixture.TestingDateTimeOffset);
        var channel = Channel.Create(testChannelId, MockAuthenticationExtensions.TestSub, "testChannelName123",
            testUploadsPlaylistId, DateTimeOffset.UtcNow);

        fixture.ApiFactory.MockCurrentChannelContext.Setup(x => x.GetRequiredChannelId())
            .Returns(testChannelId);

        var video1 = Video.Create(
            testUploadsPlaylistId,
            "video1",
            "Cooking Tutorial",
            "Learn how to cook",
            TestFixture.TestingDateTimeOffset,
            TimeSpan.FromMinutes(10),
            VideoVisibility.Public,
            ["cooking", "tutorial"],
            "22",
            "en",
            "en",
            null,
            null,
            TestFixture.TestingDateTimeOffset,
            "etag1"
        );

        var video2 = Video.Create(
            testUploadsPlaylistId,
            "video2",
            "Gaming Highlights",
            "Best gaming moments",
            TestFixture.TestingDateTimeOffset.AddDays(1),
            TimeSpan.FromMinutes(15),
            VideoVisibility.Unlisted,
            ["gaming", "highlights"],
            "23",
            "en",
            "en",
            null,
            null,
            TestFixture.TestingDateTimeOffset,
            "etag2"
        );

        var video3 = Video.Create(
            testUploadsPlaylistId,
            "video3",
            "Private Video",
            "This is private",
            TestFixture.TestingDateTimeOffset.AddDays(2),
            TimeSpan.FromMinutes(5),
            VideoVisibility.Private,
            ["private"],
            "24",
            "en",
            "en",
            null,
            null,
            TestFixture.TestingDateTimeOffset,
            "etag3"
        );

        using (var scope = fixture.ApiServices.CreateScope())
        {
            var databaseContext = scope.ServiceProvider.GetRequiredService<YouTubesterDb>();
            databaseContext.Users.Add(user);
            databaseContext.Channels.Add(channel);
            databaseContext.Videos.AddRange(video1, video2, video3);
            await databaseContext.SaveChangesAsync();
        }

        // Act - Filter by title
        var titleResponse = await fixture.HttpClient.GetAsync("/api/videos?title=cooking");

        // Assert - Title filter
        Assert.Equal(HttpStatusCode.OK, titleResponse.StatusCode);

        var titleContent = await titleResponse.Content.ReadAsStringAsync();
        var titleResult = JsonSerializer.Deserialize<PagedResult<VideoListItemDto>>(titleContent, _serializerOptions);

        Assert.NotNull(titleResult);
        Assert.Single(titleResult.Items);
        Assert.Equal("Cooking Tutorial", titleResult.Items.First().Title);

        // Act - Filter by visibility
        var visibilityResponse = await fixture.HttpClient.GetAsync("/api/videos?visibility=Public&visibility=Unlisted");

        // Assert - Visibility filter
        Assert.Equal(HttpStatusCode.OK, visibilityResponse.StatusCode);

        var visibilityContent = await visibilityResponse.Content.ReadAsStringAsync();
        var visibilityResult =
            JsonSerializer.Deserialize<PagedResult<VideoListItemDto>>(visibilityContent, _serializerOptions);

        Assert.NotNull(visibilityResult);
        Assert.Equal(2, visibilityResult.Items.Count);
        Assert.DoesNotContain(visibilityResult.Items, v => v.Title == "Private Video");

        // Act - Test pagination
        var paginationResponse = await fixture.HttpClient.GetAsync("/api/videos?pageSize=2");

        // Assert - Pagination
        Assert.Equal(HttpStatusCode.OK, paginationResponse.StatusCode);

        var paginationContent = await paginationResponse.Content.ReadAsStringAsync();
        var paginationResult =
            JsonSerializer.Deserialize<PagedResult<VideoListItemDto>>(paginationContent, _serializerOptions);

        Assert.NotNull(paginationResult);
        Assert.Equal(2, paginationResult.Items.Count);
        Assert.NotNull(paginationResult.NextPageToken);
    }

    [Fact]
    public async Task GetVideos_WithInvalidPageSize_ReturnsBadRequest()
    {
        // Arrange
        await fixture.ResetDbAsync();

        // Act
        var response = await fixture.HttpClient.GetAsync("/api/videos?pageSize=150"); // Exceeds maximum

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var responseContent = await response.Content.ReadAsStringAsync();
        Assert.Contains("error", responseContent);
    }

    [Fact]
    public async Task GetVideos_WithInvalidPageToken_ReturnsBadRequest()
    {
        // Arrange
        await fixture.ResetDbAsync();

        // Act
        var response = await fixture.HttpClient.GetAsync("/api/videos?pageToken=invalid-token");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var responseContent = await response.Content.ReadAsStringAsync();
        Assert.Contains("error", responseContent);
    }

    [Fact]
    public async Task GetVideos_WithNumericVisibilityValues_ReturnsOk()
    {
        // Arrange
        await fixture.ResetDbAsync();

        // Act - Using numeric values for visibility (Public=0, Unlisted=1)
        var response = await fixture.HttpClient.GetAsync("/api/videos?visibility=0&visibility=1");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PagedResult<VideoListItemDto>>(content, _serializerOptions);

        Assert.NotNull(result);
        Assert.Empty(result.Items); // No videos in DB, but request should be valid
    }

    private static async IAsyncEnumerable<T> CreateAsyncEnumerable<T>(IEnumerable<T> items)
    {
        foreach (var item in items)
        {
            yield return item;
        }

        await Task.CompletedTask;
    }

    private Video GetTargetVideo(string uploadPlaylistId)
    {
        return Video.Create(
            uploadPlaylistId,
            $"target{fixture.Auto.Create<string>()}"[..11],
            "Target Video Title",
            "Target Video Description",
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
            "etag-target",
            false
        );
    }

    private Video GetSourceVideo(string uploadPlaylistId)
    {
        return Video.Create(
            uploadPlaylistId,
            $"source{fixture.Auto.Create<string>()}"[..11],
            "Source Video Title",
            "Source Video Description",
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
            "etag-source",
            true
        );
    }
}
