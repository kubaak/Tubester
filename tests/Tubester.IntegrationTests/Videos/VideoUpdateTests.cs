using System.Net;
using Moq;
using Tubester.Application.Contracts.Videos;
using Tubester.IntegrationTests.TestHost;
using Xunit;

namespace Tubester.IntegrationTests.Videos;

[Collection(nameof(TestCollection))]
public class VideoUpdateTests(TestFixture fixture)
{
    private readonly TestHelpers _helpers = new(fixture.ApiServices);

    [Fact]
    public async Task UpdateVideo_ValidRequest_CallsYouTubeWithCorrectProperties()
    {
        // Arrange
        await fixture.CleanStateAsync();
        

        var targetVideo = TestHelpers.GetTargetVideo();
        await _helpers.SeedTestDataAsync(new TestDataOptions { Videos = [targetVideo] });

        const string newTitle = "Updated YouTube Title";
        const string newDescription = "Updated YouTube Description";
        var newTags = new[] { "updated-tag-one", "updated-tag-two" };

        var request = new UpdateVideoMetadataRequest(
            targetVideo.VideoId,
            newTitle,
            newDescription,
            newTags,
            null
        );

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

        // Act
        var requestMessage = new HttpRequestMessage(HttpMethod.Post, "/api/videos/update")
        {
            Content = TestHelpers.CreateJsonContent(request)
        };

        requestMessage.Headers.Add("OperationId", TestHelpers.NewOperationId());

        var response = await fixture.HttpClient.SendAsync(requestMessage);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        fixture.ApiFactory.MockYouTubeIntegration.Verify(youTubeIntegration =>
                youTubeIntegration.UpdateVideoAsync(
                    targetVideo.VideoId,
                    newTitle,
                    newDescription,
                    It.Is<IReadOnlyList<string>>(tags => tags.SequenceEqual(newTags)),
                    targetVideo.CategoryId,
                    targetVideo.DefaultLanguage,
                    targetVideo.DefaultAudioLanguage,
                    It.IsAny<CancellationToken>()),
            Times.Once);
    }
}