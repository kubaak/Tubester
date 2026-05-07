using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Tubester.Abstractions.ApplicationConfiguration;
using Tubester.Domain;
using Tubester.Integration;
using Tubester.IntegrationTests.TestHost;
using Xunit;

namespace Tubester.IntegrationTests;

/// <summary>
/// Unit tests for GeminiAiClient that use a fake HttpMessageHandler.
/// </summary>
[Collection(nameof(TestCollection))]
public class GeminiAiClientTests(TestFixture fixture)
{
    private readonly TestHelpers _helpers = new(fixture.ApiServices);

    private readonly Mock<ILogger<GeminiTextGenerationClient>> _mockLogger = new();
    private readonly IOptions<GeminiOptions> _options = Options.Create(new GeminiOptions { ApiKey = "test-api-key" });

    [Fact]
    public async Task Factory_ReturnsGemini()
    {
        // Arrange
        await fixture.CleanStateAsync();
        var configuration = ApplicationConfiguration.Create(ApplicationConfigurationKeys.AiProvider, AiProviders.Gemini,
            ConfigurationValueType.String, null,
            true, TestFixture.TestingDateTimeOffset);
        await _helpers.SeedTestDataAsync(new TestDataOptions
        {
            ApplicationConfigurations = [configuration]
        });

        var aiClientFactory = new AiTextGenerationClientFactory(fixture.WorkerServices, fixture.WorkerServices.GetRequiredService<IAiRuntimeOptionsService>());
        var client = await aiClientFactory.GetClientAsync(CancellationToken.None);
        // Act & Assert
        Assert.Equal(AiProviders.Gemini, client.Provider);
        Assert.IsType<GeminiTextGenerationClient>(client);
    }

    // [Fact]
    // public async Task SuggestMetadataAsync_WithValidResponse_ParsesTitleDescriptionAndTags()
    // {
    //     // Arrange
    //     var response = new
    //     {
    //         candidates = new[]
    //         {
    //             new
    //             {
    //                 content = new
    //                 {
    //                     parts = new[]
    //                     {
    //                         new { text = """{"title":"Test Title","description":"Test Description","tags":["tag1","tag2"]}""" }
    //                     }
    //                 }
    //             }
    //         }
    //     };
    //     
    //     var aiOptionsService = fixture.WorkerServices.GetRequiredService<IAiRuntimeOptionsService>();
    //     var client = new GeminiAiClient(_mockLogger.Object, _options, aiOptionsService);
    //
    //     // Act
    //     var result = await client.SuggestMetadataAsync(
    //         "Test context about cooking pasta",
    //         generateTitle: true,
    //         generateDescription: true,
    //         generateTags: true,
    //         CancellationToken.None);
    //
    //     // Assert
    //     Assert.Equal("Test Title", result.Title);
    //     Assert.Equal("Test Description", result.Description);
    //     Assert.Equal(2, result.Tags.Count);
    //     Assert.Contains("tag1", result.Tags);
    //     Assert.Contains("tag2", result.Tags);
    // }
    //
    // [Fact]
    // public async Task SuggestMetadataAsync_WhenTitleDisabled_ReturnsNullTitle()
    // {
    //     // Arrange
    //     SetupDefaultConfigService();
    //     var response = new
    //     {
    //         candidates = new[]
    //         {
    //             new
    //             {
    //                 content = new
    //                 {
    //                     parts = new[]
    //                     {
    //                         new { text = """{"title":"Should Be Null","description":"Test Description","tags":["tag1"]}""" }
    //                     }
    //                 }
    //             }
    //         }
    //     };
    //
    //     using var handler = new FakeHttpMessageHandler(response);
    //     var httpClient = new HttpClient(handler);
    //     var client = new GeminiAiClient(httpClient, _mockLogger.Object, _options, _mockConfigService.Object);
    //
    //     // Act
    //     var result = await client.SuggestMetadataAsync(
    //         "Test context",
    //         generateTitle: false, // Title disabled
    //         generateDescription: true,
    //         generateTags: true,
    //         CancellationToken.None);
    //
    //     // Assert - Title should be null even though model returned one
    //     Assert.Null(result.Title);
    // }
    //
    // [Fact]
    // public async Task SuggestMetadataAsync_TagsWithHashtags_NormalizesAndRemovesHashes()
    // {
    //     // Arrange
    //     SetupDefaultConfigService();
    //     var response = new
    //     {
    //         candidates = new[]
    //         {
    //             new
    //             {
    //                 content = new
    //                 {
    //                     parts = new[]
    //                     {
    //                         new { text = """{"title":null,"description":null,"tags":["#tag1","#tag2","tag3","tag4"]}""" }
    //                     }
    //                 }
    //             }
    //         }
    //     };
    //
    //     using var handler = new FakeHttpMessageHandler(response);
    //     var httpClient = new HttpClient(handler);
    //     var client = new GeminiAiClient(httpClient, _mockLogger.Object, _options, _mockConfigService.Object);
    //
    //     // Act
    //     var result = await client.SuggestMetadataAsync(
    //         "Test context",
    //         generateTitle: false,
    //         generateDescription: false,
    //         generateTags: true,
    //         CancellationToken.None);
    //
    //     // Assert - Tags should not have leading #
    //     Assert.DoesNotContain(result.Tags, t => t.StartsWith('#'));
    //     Assert.Equal(3, result.Tags.Count);
    // }
    //
    // [Fact]
    // public async Task SuggestMetadataAsync_TagsExceedingLimit_CapsAt15()
    // {
    //     // Arrange
    //     SetupDefaultConfigService();
    //     var manyTags = Enumerable.Range(1, 20).Select(i => $"tag{i}").ToArray();
    //     var response = new
    //     {
    //         candidates = new[]
    //         {
    //             new
    //             {
    //                 content = new
    //                 {
    //                     parts = new[]
    //                     {
    //                         new { text = JsonSerializer.Serialize(new { title = (string?)null, description = (string?)null, tags = manyTags }) }
    //                     }
    //                 }
    //             }
    //         }
    //     };
    //
    //     using var handler = new FakeHttpMessageHandler(response);
    //     var httpClient = new HttpClient(handler);
    //     var client = new GeminiAiClient(httpClient, _mockLogger.Object, _options, _mockConfigService.Object);
    //
    //     // Act
    //     var result = await client.SuggestMetadataAsync(
    //         "Test context",
    //         generateTitle: false,
    //         generateDescription: false,
    //         generateTags: true,
    //         CancellationToken.None);
    //
    //     // Assert - Should be capped at 15
    //     Assert.True(result.Tags.Count <= 15);
    // }
    //
    // [Fact]
    // public async Task SuggestReplyAsync_WithValidReply_ReturnsString()
    // {
    //     // Arrange
    //     SetupDefaultConfigService();
    //     var response = new
    //     {
    //         candidates = new[]
    //         {
    //             new
    //             {
    //                 content = new
    //                 {
    //                     parts = new[]
    //                     {
    //                         new { text = """{"reply":"Thanks for watching!"}""" }
    //                     }
    //                 }
    //             }
    //         }
    //     };
    //
    //     using var handler = new FakeHttpMessageHandler(response);
    //     var httpClient = new HttpClient(handler);
    //     var client = new GeminiAiClient(httpClient, _mockLogger.Object, _options, _mockConfigService.Object);
    //
    //     // Act
    //     var result = await client.SuggestReplyAsync(
    //         "Test Video",
    //         new[] { "tag1" },
    //         "Great video!",
    //         "en",
    //         CancellationToken.None);
    //
    //     // Assert
    //     Assert.Equal("Thanks for watching!", result);
    // }
    //
    // [Fact]
    // public async Task SuggestReplyAsync_WithNullReply_ReturnsNull()
    // {
    //     // Arrange
    //     SetupDefaultConfigService();
    //     var response = new
    //     {
    //         candidates = new[]
    //         {
    //             new
    //             {
    //                 content = new
    //                 {
    //                     parts = new[]
    //                     {
    //                         new { text = """{"reply":null}""" }
    //                     }
    //                 }
    //             }
    //         }
    //     };
    //
    //     using var handler = new FakeHttpMessageHandler(response);
    //     var httpClient = new HttpClient(handler);
    //     var client = new GeminiAiClient(httpClient, _mockLogger.Object, _options, _mockConfigService.Object);
    //
    //     // Act
    //     var result = await client.SuggestReplyAsync(
    //         "Test Video",
    //         new[] { "tag1" },
    //         "spam comment",
    //         "en",
    //         CancellationToken.None);
    //
    //     // Assert
    //     Assert.Null(result);
    // }
    //
    // [Fact]
    // public async Task SuggestReplyAsync_WithWhitespaceReply_ReturnsNull()
    // {
    //     // Arrange
    //     SetupDefaultConfigService();
    //     var response = new
    //     {
    //         candidates = new[]
    //         {
    //             new
    //             {
    //                 content = new
    //                 {
    //                     parts = new[]
    //                     {
    //                         new { text = """{"reply":"   "}""" }
    //                     }
    //                 }
    //             }
    //         }
    //     };
    //
    //     using var handler = new FakeHttpMessageHandler(response);
    //     var httpClient = new HttpClient(handler);
    //     var client = new GeminiAiClient(httpClient, _mockLogger.Object, _options, _mockConfigService.Object);
    //
    //     // Act
    //     var result = await client.SuggestReplyAsync(
    //         "Test Video",
    //         new[] { "tag1" },
    //         "some comment",
    //         "en",
    //         CancellationToken.None);
    //
    //     // Assert
    //     Assert.Null(result);
    // }
    //
    // [Fact]
    // public async Task SuggestPlaylistIdsAsync_WithValidIds_ReturnsFilteredIds()
    // {
    //     // Arrange
    //     SetupDefaultConfigService();
    //     var response = new
    //     {
    //         candidates = new[]
    //         {
    //             new
    //             {
    //                 content = new
    //                 {
    //                     parts = new[]
    //                     {
    //                         new { text = """{"playlistIds":["PLValid1","PLValid2","PLInvalid"]}""" }
    //                     }
    //                 }
    //             }
    //         }
    //     };
    //
    //     using var handler = new FakeHttpMessageHandler(response);
    //     var httpClient = new HttpClient(handler);
    //     var client = new GeminiAiClient(httpClient, _mockLogger.Object, _options, _mockConfigService.Object);
    //
    //     var playlists = new List<PlaylistCandidateDto>
    //     {
    //         new() { PlaylistId = "PLValid1", Name = "Valid Playlist 1" },
    //         new() { PlaylistId = "PLValid2", Name = "Valid Playlist 2" }
    //     };
    //
    //     var context = new PlaylistSuggestionContext
    //     {
    //         PromptEnrichment = "Cooking video",
    //         LatestPlaylistTitlesUsed = new List<string>()
    //     };
    //
    //     // Act
    //     var result = await client.SuggestPlaylistIdsAsync(context, playlists, CancellationToken.None);
    //
    //     // Assert - PLInvalid should be filtered out
    //     var resultList = result.ToList();
    //     Assert.Equal(2, resultList.Count);
    //     Assert.Contains("PLValid1", resultList);
    //     Assert.Contains("PLValid2", resultList);
    //     Assert.DoesNotContain(resultList, id => id == "PLInvalid");
    // }
    //
    // [Fact]
    // public async Task SuggestPlaylistIdsAsync_WithEmptyPlaylists_ReturnsEmptyList()
    // {
    //     // Arrange
    //     SetupDefaultConfigService();
    //     using var handler = new FakeHttpMessageHandler();
    //     var httpClient = new HttpClient(handler);
    //     var client = new GeminiAiClient(httpClient, _mockLogger.Object, _options, _mockConfigService.Object);
    //
    //     var context = new PlaylistSuggestionContext
    //     {
    //         PromptEnrichment = "Cooking video",
    //         LatestPlaylistTitlesUsed = new List<string>()
    //     };
    //
    //     // Act
    //     var result = await client.SuggestPlaylistIdsAsync(context, new List<PlaylistCandidateDto>(), CancellationToken.None);
    //
    //     // Assert
    //     Assert.Empty(result);
    // }
    //
    // [Fact]
    // public async Task GenerateJsonAsync_InvalidJsonResponse_ThrowsInvalidOperationException()
    // {
    //     // Arrange
    //     SetupDefaultConfigService();
    //     var response = new
    //     {
    //         candidates = new[]
    //         {
    //             new
    //             {
    //                 content = new
    //                 {
    //                     parts = new[]
    //                     {
    //                         new { text = "not valid json at all" }
    //                     }
    //                 }
    //             }
    //         }
    //     };
    //
    //     using var handler = new FakeHttpMessageHandler(response);
    //     var httpClient = new HttpClient(handler);
    //     var client = new GeminiAiClient(httpClient, _mockLogger.Object, _options, _mockConfigService.Object);
    //
    //     // Act & Assert
    //     await Assert.ThrowsAsync<InvalidOperationException>(() =>
    //         client.SuggestMetadataAsync("test", true, true, true, CancellationToken.None));
    // }
    //
    // [Fact]
    // public async Task GenerateJsonAsync_NonSuccessHttpResponse_ThrowsInvalidOperationException()
    // {
    //     // Arrange
    //     SetupDefaultConfigService();
    //     using var handler = new FakeHttpMessageHandler("error", System.Net.HttpStatusCode.BadRequest);
    //     var httpClient = new HttpClient(handler);
    //     var client = new GeminiAiClient(httpClient, _mockLogger.Object, _options, _mockConfigService.Object);
    //
    //     // Act & Assert
    //     await Assert.ThrowsAsync<InvalidOperationException>(() =>
    //         client.SuggestMetadataAsync("test", true, true, true, CancellationToken.None));
    // }
    //
    // [Fact]
    // public async Task GenerateJsonAsync_NoCandidates_ThrowsInvalidOperationException()
    // {
    //     // Arrange
    //     SetupDefaultConfigService();
    //     var response = new { candidates = Array.Empty<object>() };
    //
    //     using var handler = new FakeHttpMessageHandler(response);
    //     var httpClient = new HttpClient(handler);
    //     var client = new GeminiAiClient(httpClient, _mockLogger.Object, _options, _mockConfigService.Object);
    //
    //     // Act & Assert
    //     await Assert.ThrowsAsync<InvalidOperationException>(() =>
    //         client.SuggestMetadataAsync("test", true, true, true, CancellationToken.None));
    // }
    //
    // [Fact]
    // public async Task GetRuntimeSettingsAsync_WithMissingConfig_UsesDefaults()
    // {
    //     // Arrange
    //     _mockConfigService.Setup(x => x.GetValueAsync<string>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
    //         .ReturnsAsync((string?)null);
    //
    //     using var handler = new FakeHttpMessageHandler();
    //     var httpClient = new HttpClient(handler);
    //     var client = new GeminiAiClient(httpClient, _mockLogger.Object, _options, _mockConfigService.Object);
    //
    //     // Act
    //     var result = await client.SuggestMetadataAsync("test", true, true, true, CancellationToken.None);
    //
    //     // Assert - Should not throw, defaults should be used
    //     Assert.NotNull(result);
    // }
    //
    // [Fact]
    // public async Task GetRuntimeSettingsAsync_WithCustomConfig_UsesValues()
    // {
    //     // Arrange
    //     _mockConfigService.Setup(x => x.GetValueAsync<string>(ApplicationConfigurationKeys.AiModel, It.IsAny<CancellationToken>()))
    //         .ReturnsAsync("gemini-pro");
    //     _mockConfigService.Setup(x => x.GetValueAsync<string>(ApplicationConfigurationKeys.AiTemperature, It.IsAny<CancellationToken>()))
    //         .ReturnsAsync("0.8");
    //     _mockConfigService.Setup(x => x.GetValueAsync<string>(ApplicationConfigurationKeys.AiDetailsMaxOutputTokens, It.IsAny<CancellationToken>()))
    //         .ReturnsAsync("1000");
    //
    //     var response = new
    //     {
    //         candidates = new[]
    //         {
    //             new
    //             {
    //                 content = new
    //                 {
    //                     parts = new[]
    //                     {
    //                         new { text = """{"title":"Test"}""" }
    //                     }
    //                 }
    //             }
    //         }
    //     };
    //
    //     string? capturedUrl = null;
    //     string? capturedBody = null;
    //     using var handler = new CapturingHttpMessageHandler(response, System.Net.HttpStatusCode.OK, (url, body) =>
    //     {
    //         capturedUrl = url;
    //         capturedBody = body;
    //     });
    //     var httpClient = new HttpClient(handler);
    //     var client = new GeminiAiClient(httpClient, _mockLogger.Object, _options, _mockConfigService.Object);
    //
    //     // Act
    //     await client.SuggestMetadataAsync("test", true, false, false, CancellationToken.None);
    //
    //     // Assert - Verify custom config was used
    //     Assert.NotNull(capturedUrl);
    //     Assert.Contains("gemini-pro", capturedUrl);
    //     Assert.NotNull(capturedBody);
    //     Assert.Contains("\"temperature\":0.8", capturedBody);
    //     Assert.Contains("\"maxOutputTokens\":1000", capturedBody);
    // }

    /// <summary>
    /// Fake HttpMessageHandler for testing that returns pre-configured responses.
    /// </summary>
    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly object? _response;
        private readonly System.Net.HttpStatusCode _statusCode;

        public FakeHttpMessageHandler(object? response = null, System.Net.HttpStatusCode statusCode = System.Net.HttpStatusCode.OK)
        {
            _response = response;
            _statusCode = statusCode;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = _response != null
                ? JsonSerializer.Serialize(_response)
                : "{}";

            var response = new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json")
            };

            return Task.FromResult(response);
        }
    }

    /// <summary>
    /// HttpMessageHandler that captures the request URL and body for verification.
    /// </summary>
    private sealed class CapturingHttpMessageHandler : HttpMessageHandler
    {
        private readonly object? _response;
        private readonly System.Net.HttpStatusCode _statusCode;
        private readonly Action<string, string>? _captureCallback;

        public CapturingHttpMessageHandler(object? response, System.Net.HttpStatusCode statusCode, Action<string, string>? captureCallback = null)
        {
            _response = response;
            _statusCode = statusCode;
            _captureCallback = captureCallback;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri?.ToString() ?? "";
            var body = request.Content != null
                ? await request.Content.ReadAsStringAsync(cancellationToken)
                : "";

            _captureCallback?.Invoke(url, body);

            var content = _response != null
                ? JsonSerializer.Serialize(_response)
                : "{}";

            var response = new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json")
            };

            return response;
        }
    }
}