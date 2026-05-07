using System.Net;
using Tubester.Abstractions.Credits;
using Tubester.Application.Contracts.Videos;
using Tubester.IntegrationTests.TestHost;
using Xunit;

namespace Tubester.IntegrationTests;

[Collection(nameof(TestCollection))]
public class VideoCopyDetailsTests(TestFixture fixture)
{
    private readonly TestHelpers _helpers = new(fixture.ApiServices);

    [Fact]
    public async Task CopyTemplate_ValidRequest_LogsAnalytics()
    {
        // Arrange
        await fixture.CleanStateAsync();

        var sourceVideo = TestHelpers.GetSourceVideo();
        var targetVideo = TestHelpers.GetTargetVideo();

        await _helpers.SeedTestDataAsync(new TestDataOptions
        {
            Videos = [sourceVideo, targetVideo]
        });

        var request = new CopyVideoTemplateRequest(
            sourceVideo.VideoId,
            targetVideo.VideoId,
            true,
            true
        );

        // Act
        var response = await fixture.HttpClient.PostAsync("/api/videos/copy-template",
            TestHelpers.CreateJsonContent(request));

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var responseContent = await response.Content.ReadAsStringAsync();
        Assert.False(string.IsNullOrWhiteSpace(responseContent));

        //TODO currently not being billed
        // await _helpers.VerifyLedgerAndWalletAfterDeductionAsync(nameof(CreditActionType.CopyTemplateExecuted), 
        //     TestConstants.CopyTemplateExecutedCost, targetVideo.VideoId);

        TestHelpers.SetProperty(targetVideo, nameof(targetVideo.CategoryId), sourceVideo.CategoryId); //TODO implement category copy option
        TestHelpers.SetVideoProperties(targetVideo, sourceVideo.Title!, targetVideo.Description!, sourceVideo.Tags.ToArray());
        await _helpers.AssertVideoAsync(targetVideo);
        await _helpers.AssertUserEventAsync(CreditActionType.CopyTemplateExecuted, TestConstants.UserId, targetVideo.VideoId);
    }

    [Fact]
    public async Task CopyTemplate_EmptySourceVideoId_ReturnsBadRequest()
    {
        // Arrange
        var request = new CopyVideoTemplateRequest(
            "",
            "targetVideoId456"
        );

        // Act
        var response = await fixture.HttpClient.PostAsync(
            "/api/videos/copy-template",
            TestHelpers.CreateJsonContent(request));

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var responseContent = await response.Content.ReadAsStringAsync();
        Assert.Contains("SourceVideoId is required", responseContent);
    }

    [Fact]
    public async Task CopyTemplate_EmptyTargetVideoId_ReturnsBadRequest()
    {
        // Arrange
        var request = new CopyVideoTemplateRequest(
            "sourceVideoId123",
            ""
        );

        // Act
        var response = await fixture.HttpClient.PostAsync(
            "/api/videos/copy-template",
            TestHelpers.CreateJsonContent(request));

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var responseContent = await response.Content.ReadAsStringAsync();
        Assert.Contains("TargetVideoId is required", responseContent);
    }

    [Fact]
    public async Task CopyTemplate_SameSourceAndTargetVideoId_ReturnsBadRequest()
    {
        // Arrange
        var request = new CopyVideoTemplateRequest(
            "sameVideoId123",
            "sameVideoId123"
        );

        // Act
        var response = await fixture.HttpClient.PostAsync(
            "/api/videos/copy-template",
            TestHelpers.CreateJsonContent(request));

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var responseContent = await response.Content.ReadAsStringAsync();
        Assert.Contains("SourceVideoId and TargetVideoId must be different", responseContent);
    }

    [Fact]
    public async Task CopyTemplate_WhenCopyTitleFalse_KeepsTargetTitle()
    {
        // Arrange
        await fixture.CleanStateAsync();
        var sourceVideo = TestHelpers.GetSourceVideo();
        var targetVideo = TestHelpers.GetTargetVideo();

        await _helpers.SeedTestDataAsync(new TestDataOptions
        {
            Videos = [sourceVideo, targetVideo]
        });

        var request = new CopyVideoTemplateRequest(
            sourceVideo.VideoId,
            targetVideo.VideoId,
            CopyTags: true,
            CopyTitle: false,
            CopyDescription: false
        );

        // Act
        var response = await fixture.HttpClient.PostAsync(
            "/api/videos/copy-template",
            TestHelpers.CreateJsonContent(request));

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await TestHelpers.DeserializeAsync<CopyVideoTemplateResult>(response);

        Assert.NotNull(result);
        Assert.Equal(targetVideo.Title, result.FinalTitle);
        Assert.False(result.TitleCopied);
        Assert.Equivalent(sourceVideo.Tags, result.AppliedTags);
    }

    [Fact]
    public async Task CopyTemplate_WhenCopyDescriptionFalse_KeepsTargetDescription()
    {
        // Arrange
        await fixture.CleanStateAsync();
        var sourceVideo = TestHelpers.GetSourceVideo();
        var targetVideo = TestHelpers.GetTargetVideo();

        await _helpers.SeedTestDataAsync(new TestDataOptions
        {
            Videos = [sourceVideo, targetVideo]
        });

        var request = new CopyVideoTemplateRequest(
            sourceVideo.VideoId,
            targetVideo.VideoId,
            CopyTags: false,
            CopyTitle: true,
            CopyDescription: false
        );

        // Act
        var response = await fixture.HttpClient.PostAsync(
            "/api/videos/copy-template",
            TestHelpers.CreateJsonContent(request));

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await TestHelpers.DeserializeAsync<CopyVideoTemplateResult>(response);

        Assert.NotNull(result);
        Assert.Equal(sourceVideo.Title, result.FinalTitle);
        Assert.True(result.TitleCopied);
        Assert.Equal(targetVideo.Description, result.FinalDescription);
        Assert.False(result.DescriptionCopied);
        Assert.Equivalent(targetVideo.Tags, result.AppliedTags);
    }
}