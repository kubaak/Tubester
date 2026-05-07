using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Tubester.Abstractions.Credits;
using Tubester.Application.Contracts.Videos;
using Tubester.Application.Jobs;
using Tubester.IntegrationTests.TestHost;
using Tubester.Persistence;
using Xunit;

namespace Tubester.IntegrationTests;

[Collection(nameof(TestCollection))]
public class VideoAiTemplateTests(TestFixture fixture)
{
    private readonly TestHelpers _helpers = new(fixture.ApiServices);

    [Fact]
    public async Task AiTemplate_ValidRequest_EnqueuesAiTemplateJob_AndLogsAnalytics()
    {
        // Arrange
        await fixture.CleanStateAsync();
        var targetVideo = TestHelpers.GetTargetVideo();

        await _helpers.SeedTestDataAsync(new TestDataOptions
        {
            Videos = [targetVideo]
        });

        var request = new AiVideoTemplateRequest
        {
            TargetVideoId = targetVideo.VideoId,
            PromptEnrichment = "Generate better metadata"
        };

        // Act
        var requestMessage = new HttpRequestMessage(HttpMethod.Post, "/api/videos/ai-template")
        {
            Content = TestHelpers.CreateJsonContent(request)
        };

        requestMessage.Headers.Add("OperationId", TestHelpers.NewOperationId());

        var videosEndpointResponse = await fixture.HttpClient.SendAsync(requestMessage);

        // Assert
        Assert.Equal(HttpStatusCode.Accepted, videosEndpointResponse.StatusCode);

        var capturedJobs = fixture.CapturingJobClient.GetEnqueued<AiTemplateJob>();
        Assert.Single(capturedJobs);
        Assert.Equal(nameof(AiTemplateJob.Run), capturedJobs[0].Job.Method.Name);

        var enqueuedRequest = Assert.IsType<AiVideoDetailsRequest>(capturedJobs[0].Job.Args.SingleOrDefault(a => a is AiVideoDetailsRequest));
        Assert.Equal(TestConstants.ChannelId, enqueuedRequest.ChannelId);
        Assert.Equal(TestConstants.UploadsPlaylistId, enqueuedRequest.UploadPlaylistId);
        Assert.Equal(request.TargetVideoId, enqueuedRequest.TargetVideoId);
        Assert.Equal(request.PromptEnrichment, enqueuedRequest.PromptEnrichment);
        Assert.Equal(request.GenerateTitle, enqueuedRequest.GenerateTitle);
        Assert.Equal(request.GenerateDescription, enqueuedRequest.GenerateDescription);
        Assert.Equal(request.GenerateTags, enqueuedRequest.GenerateTags);

        using var serviceScope = fixture.ApiServices.CreateScope();
        var databaseContext = serviceScope.ServiceProvider.GetRequiredService<TubesterDb>();
        var videoInDatabase = await databaseContext.Videos.FindAsync(targetVideo.VideoId);

        Assert.NotNull(videoInDatabase);
        Assert.True(
            videoInDatabase.IsAiTitleInProgress ||
            videoInDatabase.IsAiDescriptionInProgress ||
            videoInDatabase.IsAiTagsInProgress);

        await _helpers.AssertUserEventAsync(CreditActionType.AiTemplateEnqueued, TestConstants.UserId, targetVideo.VideoId);
    }

    [Fact]
    public async Task AiTemplate_EmptyTargetVideoId_ReturnsBadRequest()
    {
        // Arrange
        var request = new AiVideoTemplateRequest
        {
            TargetVideoId = "",
            PromptEnrichment = "Generate metadata"
        };

        // Act
        var requestMessage = new HttpRequestMessage(HttpMethod.Post, "/api/videos/ai-template")
        {
            Content = TestHelpers.CreateJsonContent(request)
        };

        requestMessage.Headers.Add("OperationId", TestHelpers.NewOperationId());

        var videosEndpointResponse = await fixture.HttpClient.SendAsync(requestMessage);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, videosEndpointResponse.StatusCode);

        var responseContent = await videosEndpointResponse.Content.ReadAsStringAsync();
        Assert.Contains("TargetVideoId is required", responseContent);
    }

    [Fact]
    public async Task AiTemplate_EmptyPromptEnrichment_ReturnsBadRequest()
    {
        // Arrange
        var request = new AiVideoTemplateRequest
        {
            TargetVideoId = "targetVideoId123",
            PromptEnrichment = ""
        };

        // Act
        var requestMessage = new HttpRequestMessage(HttpMethod.Post, "/api/videos/ai-template")
        {
            Content = TestHelpers.CreateJsonContent(request)
        };

        requestMessage.Headers.Add("OperationId", TestHelpers.NewOperationId());

        var videosEndpointResponse = await fixture.HttpClient.SendAsync(requestMessage);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, videosEndpointResponse.StatusCode);

        var responseContent = await videosEndpointResponse.Content.ReadAsStringAsync();
        Assert.Contains("PromptEnrichment is required", responseContent);
    }

    [Fact]
    public async Task AiTemplate_TargetVideoNotFound_ReturnsBadRequest()
    {
        // Arrange
        await fixture.CleanStateAsync();
        await _helpers.SeedTestDataAsync();
        var request = new AiVideoTemplateRequest
        {
            TargetVideoId = "non-existent-video-id",
            PromptEnrichment = "Generate metadata"
        };

        // Act
        var requestMessage = new HttpRequestMessage(HttpMethod.Post, "/api/videos/ai-template")
        {
            Content = TestHelpers.CreateJsonContent(request)
        };

        requestMessage.Headers.Add("OperationId", TestHelpers.NewOperationId());

        var videosEndpointResponse = await fixture.HttpClient.SendAsync(requestMessage);

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, videosEndpointResponse.StatusCode);

        var responseContent = await videosEndpointResponse.Content.ReadAsStringAsync();

        Assert.Contains(
            $"Target video {request.TargetVideoId} not found for current channel or one of the requested AI operations is already in progress.",
            responseContent);
    }

    [Fact]
    public async Task AiTemplate_WithSuggestPlaylists_CallsPlaylistSuggestion()
    {
        // Arrange
        await fixture.CleanStateAsync();
        var playlist1 = TestHelpers.GetPlaylist("PL1");
        var playlist2 = TestHelpers.GetPlaylist("PL2");
        await _helpers.SeedTestDataAsync(new TestDataOptions
        {
            Playlists = [playlist1, playlist2]
        });


        var request = new AiVideoTemplateRequest
        {
            TargetVideoId = TestConstants.TargetVideoId,
            PromptEnrichment = "Generate metadata",
            SuggestPlaylists = true
        };

        // Act
        var requestMessage = new HttpRequestMessage(HttpMethod.Post, "/api/videos/ai-template")
        {
            Content = TestHelpers.CreateJsonContent(request)
        };

        requestMessage.Headers.Add("OperationId", TestHelpers.NewOperationId());

        var response = await fixture.HttpClient.SendAsync(requestMessage);

        // Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var capturedTemplateJobs = fixture.CapturingJobClient.GetEnqueued<AiTemplateJob>();
        Assert.Single(capturedTemplateJobs);

        var enqueuedRequest = Assert.IsType<AiVideoDetailsRequest>(capturedTemplateJobs[0].Job.Args.Single(a => a is AiVideoDetailsRequest));
        Assert.True(enqueuedRequest.SuggestPlaylists);

        var capturedPlaylistSuggestionJobs = fixture.CapturingJobClient.GetEnqueued<AiPlaylistSuggestionJob>();
        Assert.Single(capturedPlaylistSuggestionJobs);
        var suggestionRequest = Assert.IsType<PlaylistSuggestionRequest>(capturedPlaylistSuggestionJobs[0].Job.Args.Single(a => a is PlaylistSuggestionRequest));
        Assert.Equal(TestConstants.TargetVideoId, suggestionRequest.TargetVideoId);
        Assert.Equal(TestConstants.ChannelId, suggestionRequest.ChannelId);
        Assert.Equal(TestConstants.UploadsPlaylistId, suggestionRequest.UploadPlaylistId);
        Assert.Equal(request.PromptEnrichment, suggestionRequest.PromptEnrichment);
    }

    [Fact]
    public async Task AiTemplate_SuggestPlaylists_NoDetailsGeneration()
    {
        // Arrange
        await fixture.CleanStateAsync();
        var playlist1 = TestHelpers.GetPlaylist("PL1");
        await _helpers.SeedTestDataAsync(new TestDataOptions
        {
            Playlists = [playlist1]
        });


        var request = new AiVideoTemplateRequest
        {

            TargetVideoId = TestConstants.TargetVideoId,
            PromptEnrichment = "Generate metadata",
            GenerateTitle = false,
            GenerateDescription = false,
            GenerateTags = false,
            SuggestPlaylists = true
        };

        // Act
        var requestMessage = new HttpRequestMessage(HttpMethod.Post, "/api/videos/ai-template")
        {
            Content = TestHelpers.CreateJsonContent(request)
        };

        requestMessage.Headers.Add("OperationId", TestHelpers.NewOperationId());

        var response = await fixture.HttpClient.SendAsync(requestMessage);

        // Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var capturedTemplateJobs = fixture.CapturingJobClient.GetEnqueued<AiTemplateJob>();
        Assert.Empty(capturedTemplateJobs);

        var capturedPlaylistSuggestionJobs = fixture.CapturingJobClient.GetEnqueued<AiPlaylistSuggestionJob>();
        Assert.Single(capturedPlaylistSuggestionJobs);
        var suggestionRequest = Assert.IsType<PlaylistSuggestionRequest>(capturedPlaylistSuggestionJobs[0].Job.Args.Single(a => a is PlaylistSuggestionRequest));
        Assert.Equal(TestConstants.TargetVideoId, suggestionRequest.TargetVideoId);
        Assert.Equal(TestConstants.ChannelId, suggestionRequest.ChannelId);
        Assert.Equal(TestConstants.UploadsPlaylistId, suggestionRequest.UploadPlaylistId);
        Assert.Equal(request.PromptEnrichment, suggestionRequest.PromptEnrichment);
    }

    [Fact]
    public async Task AiTemplate_WithoutSuggestPlaylists_DoesNotCallPlaylistSuggestion()
    {
        // Arrange
        await fixture.CleanStateAsync();
        await _helpers.SeedTestDataAsync();

        var request = new AiVideoTemplateRequest
        {
            TargetVideoId = TestConstants.TargetVideoId,
            PromptEnrichment = "Generate metadata",
            SuggestPlaylists = false
        };

        // Act
        var requestMessage = new HttpRequestMessage(HttpMethod.Post, "/api/videos/ai-template")
        {
            Content = TestHelpers.CreateJsonContent(request)
        };

        requestMessage.Headers.Add("OperationId", TestHelpers.NewOperationId());

        var response = await fixture.HttpClient.SendAsync(requestMessage);

        // Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var capturedTemplateJobs = fixture.CapturingJobClient.GetEnqueued<AiTemplateJob>();
        Assert.Single(capturedTemplateJobs);

        var enqueuedRequest = Assert.IsType<AiVideoDetailsRequest>(capturedTemplateJobs[0].Job.Args.Single(a => a is AiVideoDetailsRequest));
        Assert.False(enqueuedRequest.SuggestPlaylists);

        var capturedPlaylistSuggestionJobs = fixture.CapturingJobClient.GetEnqueued<AiPlaylistSuggestionJob>();
        Assert.Empty(capturedPlaylistSuggestionJobs);
    }

    [Fact]
    public async Task AiTemplate_WhenNoFieldsSelected_ReturnsBadRequest()
    {
        // Arrange
        await fixture.CleanStateAsync();
        var request = new AiVideoTemplateRequest
        {
            TargetVideoId = TestConstants.TargetVideoId,
            PromptEnrichment = "Generate metadata",
            GenerateTitle = false,
            GenerateDescription = false,
            GenerateTags = false,
            SuggestPlaylists = false
        };

        // Act
        var requestMessage = new HttpRequestMessage(HttpMethod.Post, "/api/videos/ai-template")
        {
            Content = TestHelpers.CreateJsonContent(request)
        };

        requestMessage.Headers.Add("OperationId", TestHelpers.NewOperationId());

        var response = await fixture.HttpClient.SendAsync(requestMessage);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await _helpers.AssertProblemDetailsAsync(
            response,
            StatusCodes.Status400BadRequest,
            "Bad request",
            "At least one AI template action must be requested.");

        var capturedTemplateJobs = fixture.CapturingJobClient.GetEnqueued<AiTemplateJob>();
        Assert.Empty(capturedTemplateJobs);

        var capturedPlaylistSuggestionJobs = fixture.CapturingJobClient.GetEnqueued<AiPlaylistSuggestionJob>();
        Assert.Empty(capturedPlaylistSuggestionJobs);
    }

    [Fact]
    public async Task AiTemplateEnqueue_WithSufficientCredits_DeductsCreditsAndAppendsLedgerEntry()
    {
        // Arrange
        await fixture.CleanStateAsync();
        await _helpers.SeedTestDataAsync();

        var request = new AiVideoTemplateRequest
        {
            TargetVideoId = TestConstants.TargetVideoId,
            PromptEnrichment = "Generate better metadata for credits test"
        };

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, "/api/videos/ai-template")
        {
            Content = TestHelpers.CreateJsonContent(request)
        };

        requestMessage.Headers.Add("OperationId", TestHelpers.NewOperationId());

        // Act
        var response = await fixture.HttpClient.SendAsync(requestMessage);

        // Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        await _helpers.VerifyLedgerAndWalletAfterDeductionAsync(nameof(CreditActionType.AiTemplateEnqueued),
            TestConstants.AiTemplateCost, TestConstants.TargetVideoId);
    }
}