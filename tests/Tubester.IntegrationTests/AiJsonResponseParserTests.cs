using Microsoft.Extensions.DependencyInjection;
using Tubester.Integration;
using Tubester.IntegrationTests.TestHost;
using Xunit;

namespace Tubester.IntegrationTests;

[Collection(nameof(TestCollection))]
public sealed class AiJsonResponseParserTests(TestFixture fixture)
{
    [Fact]
    public async Task DeserializeModelResponse_WithValidJson_DeserializesJson()
    {
        // Arrange
        await fixture.CleanStateAsync();

        const string response = """
        {
          "title": "AI Generated Title",
          "description": "AI Generated Description",
          "tags": [
            "tag-01",
            "tag-02"
          ]
        }
        """;

        var parser = CreateParser();

        // Act
        var result = parser.DeserializeModelResponse<TestAiMetadataJsonResult>(response);

        // Assert
        Assert.Equal("AI Generated Title", result.Title);
        Assert.Equal("AI Generated Description", result.Description);
        Assert.Equal(["tag-01", "tag-02"], result.Tags);
    }

    [Fact]
    public async Task DeserializeModelResponse_WithJsonCodeFence_DeserializesJson()
    {
        // Arrange
        await fixture.CleanStateAsync();

        const string response = """
        ```json
        {
          "title": "Fenced Title",
          "description": "Fenced Description",
          "tags": [
            "json",
            "fence"
          ]
        }
        ```
        """;

        var parser = CreateParser();

        // Act
        var result = parser.DeserializeModelResponse<TestAiMetadataJsonResult>(response);

        // Assert
        Assert.Equal("Fenced Title", result.Title);
        Assert.Equal("Fenced Description", result.Description);
        Assert.Equal(["json", "fence"], result.Tags);
    }

    [Fact]
    public async Task DeserializeModelResponse_WithTextAroundJson_ExtractsAndDeserializesJson()
    {
        // Arrange
        await fixture.CleanStateAsync();

        const string response = """
        Sure, here is the metadata:

        {
          "title": "Extracted Title",
          "description": "Extracted Description",
          "tags": [
            "extract",
            "metadata"
          ]
        }

        Hope this helps.
        """;

        var parser = CreateParser();

        // Act
        var result = parser.DeserializeModelResponse<TestAiMetadataJsonResult>(response);

        // Assert
        Assert.Equal("Extracted Title", result.Title);
        Assert.Equal("Extracted Description", result.Description);
        Assert.Equal(["extract", "metadata"], result.Tags);
    }

    [Fact]
    public async Task DeserializeModelResponse_WhenAiReturnsTruncatedTagsJson_RepairsAndDeserializesJson()
    {
        // Arrange
        await fixture.CleanStateAsync();

        const string response = """
        {
          "title": "AI Generated Title",
          "description": "AI Generated Description",
          "tags": [
            "tag-01",
            "tag-02",
            "tag-03",
            "tag-04",
            "tag-05",
            "tag-06",
            "tag-07",
            "tag-08",
            "tag-09",
            "tag-10",
            "tag-11",
            "tag-12",
            "tag-13",
            "tag-14",
            "tag-15",
            "tag-16",
            "tag-17",
            "tag-18",
            "tag-19",
            "tag-20",
            "tag-21",
            "tag-22"
        """;

        var parser = CreateParser();

        // Act
        var result = parser.DeserializeModelResponse<TestAiMetadataJsonResult>(response);

        // Assert
        Assert.Equal("AI Generated Title", result.Title);
        Assert.Equal("AI Generated Description", result.Description);
        Assert.Equal(22, result.Tags.Count);
        Assert.Equal("tag-01", result.Tags[0]);
        Assert.Equal("tag-22", result.Tags[^1]);
    }

    [Fact]
    public void DeserializeModelJson_WhenClosingBraceIsMissing_RepairsJson()
    {
        var parser = CreateParser();

        var result = parser.DeserializeModelResponse<TestAiMetadataJsonResult>(
            """
               {
                 "title": "Missing Closing Brace",
                 "description": "Description without final object brace",
                 "tags": []
            """
            );

        Assert.Equal("Missing Closing Brace", result.Title);
        Assert.Equal("Description without final object brace", result.Description);
        Assert.Empty(result.Tags);
    }

    [Fact]
    public void DeserializeModelResponse_WhenStringValueIsTruncated_Throws()
    {
        var parser = CreateParser();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            parser.DeserializeModelResponse<TestAiMetadataJsonResult>("""
                                                                      {
                                                                        "title": "Truncated String Title",
                                                                        "description": "This description was cut off
                                                                      """));

        Assert.Contains("Failed to deserialize AI JSON response", exception.Message);
    }

    [Fact]
    public async Task DeserializeModelResponse_WithEmptyResponse_ThrowsInvalidOperationException()
    {
        // Arrange
        await fixture.CleanStateAsync();

        var parser = CreateParser();

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() =>
            parser.DeserializeModelResponse<TestAiMetadataJsonResult>("   "));

        // Assert
        Assert.Equal("AI returned an empty response.", exception.Message);
    }

    private IAiJsonResponseParser CreateParser()
    {
        using var scope = fixture.WorkerServices.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IAiJsonResponseParser>();
    }

    private sealed class TestAiMetadataJsonResult
    {
        public string? Title { get; init; }

        public string? Description { get; init; }

        public List<string> Tags { get; init; } = [];
    }
}