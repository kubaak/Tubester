using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Tubester.Abstractions.ApplicationConfiguration;
using Tubester.Abstractions.Videos;
using Tubester.Application.Jobs;
using Tubester.Domain;
using Tubester.IntegrationTests.TestHost;
using Xunit;

namespace Tubester.IntegrationTests;

[Collection(nameof(TestCollection))]
public class AiTemplateJobTests(TestFixture fixture)
{
    private readonly TestHelpers _helpers = new(fixture.WorkerServices);

    private const string PromptEnrichment = "Generate better metadata";
    private const string TitleAndDescriptionPromptEnrichment = "Generate title and description only";
    private const string MetadataPromptEnrichment = "Generate metadata";

    private const string SuggestedTitle = "AI Generated Title";
    private const string SuggestedDescription = "AI Generated Description with detailed content";

    private const string AlternativeSuggestedTitle = "AI Title";
    private const string AlternativeSuggestedDescription = "AI Description";

    private const string AiTag1 = "ai-tag-1";
    private const string AiTag2 = "ai-tag-2";
    private const string AiTag3 = "ai-tag-3";

    private const string Tag1 = "tag1";
    private const string Tag2 = "tag2";

    private const string SimulatedAiTextGenerationClientFailureMessage = "Simulated AI client failure";

    [Fact]
    public async Task Run_WithValidRequest_UpdatesVideoWithSuggestedMetadata()
    {
        // Arrange
        await fixture.CleanStateAsync();


        var targetVideo = TestHelpers.GetTargetVideo();
        var testData = await _helpers.SeedTestDataAsync(new TestDataOptions
        {
            Videos = [targetVideo]
        });

        var suggestedTags = new List<string> { AiTag1, AiTag2, AiTag3 };
        var jsonResponse = JsonSerializer.Serialize(new { title = SuggestedTitle, description = SuggestedDescription, tags = suggestedTags });

        fixture.WorkerFactory.MockAiTextGenerationClient
            .Setup(client => client.GenerateTextAsync(
                AiOperation.Metadata,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateMockResult(jsonResponse));

        var request = new AiVideoDetailsRequest(
            testData.Channel.ChannelId,
            testData.Channel.UploadsPlaylistId,
            targetVideo.VideoId,
            PromptEnrichment,
            GenerateTitle: true,
            GenerateDescription: true,
            GenerateTags: true,
            SuggestPlaylists: false);

        using var scope = fixture.WorkerServices.CreateScope();
        var videoRepository = scope.ServiceProvider.GetRequiredService<IVideoRepository>();
        await videoRepository.TryAddAiOperationsInProgressAsync(testData.Channel.UploadsPlaylistId, targetVideo.VideoId,
            AiVideoOperationFlags.Title | AiVideoOperationFlags.Description | AiVideoOperationFlags.Tags, CancellationToken.None);

        // Act
        using var jobScope = fixture.WorkerServices.CreateScope();
        var aiTemplateJob = jobScope.ServiceProvider.GetRequiredService<AiTemplateJob>();
        await aiTemplateJob.Run(request, new Hangfire.JobCancellationToken(false));

        // Assert
        TestHelpers.SetVideoProperties(targetVideo, SuggestedTitle, SuggestedDescription, suggestedTags.ToArray());
        TestHelpers.SetProperty(targetVideo, nameof(targetVideo.IsDirty), true);
        await _helpers.AssertVideoAsync(targetVideo);

        fixture.WorkerFactory.MockAiTextGenerationClient.Verify(
            client => client.GenerateTextAsync(
                AiOperation.Metadata,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Run_WhenGenerateTitleFalse_KeepsOriginalTitle()
    {
        // Arrange
        await fixture.CleanStateAsync();


        var targetVideo = TestHelpers.GetTargetVideo();
        await _helpers.SeedTestDataAsync(new TestDataOptions
        {
            Videos = [targetVideo]
        });

        var suggestedTags = new List<string> { AiTag1, AiTag2, AiTag3 };
        var jsonResponse = JsonSerializer.Serialize(new { title = AlternativeSuggestedTitle, description = SuggestedDescription, tags = suggestedTags });

        fixture.WorkerFactory.MockAiTextGenerationClient
            .Setup(client => client.GenerateTextAsync(
                AiOperation.Metadata,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateMockResult(jsonResponse));

        var request = new AiVideoDetailsRequest(
            TestConstants.ChannelId,
            TestConstants.UploadsPlaylistId,
            targetVideo.VideoId,
            PromptEnrichment,
            GenerateTitle: false,
            GenerateDescription: true,
            GenerateTags: true,
            SuggestPlaylists: false);

        // Act
        using var jobScope = fixture.WorkerServices.CreateScope();
        var aiTemplateJob = jobScope.ServiceProvider.GetRequiredService<AiTemplateJob>();
        await aiTemplateJob.Run(request, new Hangfire.JobCancellationToken(false));

        // Assert
        TestHelpers.SetVideoProperties(targetVideo, targetVideo.Title!, SuggestedDescription, suggestedTags.ToArray());
        TestHelpers.SetProperty(targetVideo, nameof(targetVideo.IsDirty), true);
        await _helpers.AssertVideoAsync(targetVideo);
    }

    [Fact]
    public async Task Run_WhenGenerateDescriptionFalse_KeepsOriginalDescription()
    {
        // Arrange
        await fixture.CleanStateAsync();


        var targetVideo = TestHelpers.GetTargetVideo();
        await _helpers.SeedTestDataAsync(new TestDataOptions
        {
            Videos = [targetVideo]
        });

        var suggestedTags = new List<string> { Tag1, Tag2 };
        var jsonResponse = JsonSerializer.Serialize(new { title = AlternativeSuggestedTitle, description = AlternativeSuggestedDescription, tags = suggestedTags });

        fixture.WorkerFactory.MockAiTextGenerationClient
            .Setup(client => client.GenerateTextAsync(
                AiOperation.Metadata,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateMockResult(jsonResponse));

        var request = new AiVideoDetailsRequest(
            TestConstants.ChannelId,
            TestConstants.UploadsPlaylistId,
            targetVideo.VideoId,
            PromptEnrichment,
            GenerateTitle: true,
            GenerateDescription: false,
            GenerateTags: true,
            SuggestPlaylists: false);

        // Act
        using var jobScope = fixture.WorkerServices.CreateScope();
        var aiTemplateJob = jobScope.ServiceProvider.GetRequiredService<AiTemplateJob>();
        await aiTemplateJob.Run(request, new Hangfire.JobCancellationToken(false));

        // Assert
        TestHelpers.SetVideoProperties(
            targetVideo,
            AlternativeSuggestedTitle,
            targetVideo.Description!,
            suggestedTags.ToArray());

        TestHelpers.SetProperty(targetVideo, nameof(targetVideo.IsDirty), true);
        await _helpers.AssertVideoAsync(targetVideo);
    }

    [Fact]
    public async Task Run_WhenGenerateTagsFalse_KeepsOriginalTags()
    {
        // Arrange
        await fixture.CleanStateAsync();


        var targetVideo = TestHelpers.GetTargetVideo();
        var originalTags = targetVideo.Tags;

        await _helpers.SeedTestDataAsync(new TestDataOptions
        {
            Videos = [targetVideo]
        });

        var jsonResponse = JsonSerializer.Serialize(new { title = AlternativeSuggestedTitle, description = AlternativeSuggestedDescription, tags = new List<string> { AiTag1, AiTag2 } });

        fixture.WorkerFactory.MockAiTextGenerationClient
            .Setup(client => client.GenerateTextAsync(
                AiOperation.Metadata,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateMockResult(jsonResponse));

        var request = new AiVideoDetailsRequest(
            TestConstants.ChannelId,
            TestConstants.UploadsPlaylistId,
            targetVideo.VideoId,
            PromptEnrichment,
            GenerateTitle: true,
            GenerateDescription: true,
            GenerateTags: false,
            SuggestPlaylists: false);

        // Act
        using var jobScope = fixture.WorkerServices.CreateScope();
        var aiTemplateJob = jobScope.ServiceProvider.GetRequiredService<AiTemplateJob>();
        await aiTemplateJob.Run(request, new Hangfire.JobCancellationToken(false));

        // Assert
        TestHelpers.SetVideoProperties(targetVideo, AlternativeSuggestedTitle, AlternativeSuggestedDescription, originalTags);
        TestHelpers.SetProperty(targetVideo, nameof(targetVideo.IsDirty), true);
        await _helpers.AssertVideoAsync(targetVideo);
    }

    [Fact]
    public async Task Run_WhenAiClientFails_ThrowsException()
    {
        // Arrange
        await fixture.CleanStateAsync();


        var targetVideo = TestHelpers.GetTargetVideo();
        await _helpers.SeedTestDataAsync(new TestDataOptions
        {
            Videos = [targetVideo]
        });

        fixture.WorkerFactory.MockAiTextGenerationClient
            .Setup(client => client.GenerateTextAsync(
                AiOperation.Metadata,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception(SimulatedAiTextGenerationClientFailureMessage));

        var request = new AiVideoDetailsRequest(
            TestConstants.ChannelId,
            TestConstants.UploadsPlaylistId,
            targetVideo.VideoId,
            PromptEnrichment,
            GenerateTitle: true,
            GenerateDescription: true,
            GenerateTags: true,
            SuggestPlaylists: false);

        // Act & Assert
        using var jobScope = fixture.WorkerServices.CreateScope();
        var aiTemplateJob = jobScope.ServiceProvider.GetRequiredService<AiTemplateJob>();

        var exception = await Assert.ThrowsAsync<Exception>(() =>
            aiTemplateJob.Run(request, new Hangfire.JobCancellationToken(false)));

        Assert.Equal(SimulatedAiTextGenerationClientFailureMessage, exception.Message);
        await _helpers.AssertVideoAsync(targetVideo);
    }
}