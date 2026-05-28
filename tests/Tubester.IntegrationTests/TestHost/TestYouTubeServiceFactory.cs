using Google.Apis.Services;
using Google.Apis.YouTube.v3;
using Tubester.Integration;

namespace Tubester.IntegrationTests.TestHost;

/// <summary>
/// A test implementation of IYouTubeServiceFactory that returns real YouTubeService
/// instances configured with fake HTTP. This allows integration tests to test
/// YouTubeIntegration logic without calling the real YouTube API.
/// </summary>
public sealed class TestYouTubeServiceFactory(StubHttpMessageHandler handler) : IYouTubeServiceFactory
{
    /// <summary>
    /// Gets the list of Create calls made to this factory.
    /// </summary>
    public List<(string AccessToken, string Scope)> Calls { get; } = [];

    /// <summary>
    /// Creates a YouTubeService configured with fake HTTP for testing.
    /// </summary>
    public YouTubeService Create(string accessToken, string scope)
    {
        Calls.Add((accessToken, scope));

        return new YouTubeService(new BaseClientService.Initializer
        {
            HttpClientFactory = new FakeGoogleHttpClientFactory(handler),
            ApplicationName = "Tubester.Tests"
        });
    }

    /// <summary>
    /// Resets the factory by clearing all recorded calls.
    /// </summary>
    public void Reset()
    {
        Calls.Clear();
    }

    /// <summary>
    /// Creates a YouTubeService for testing - async version not used in tests.
    /// </summary>
    public Task<YouTubeService> CreateAsync(string userId, CancellationToken cancellationToken)
    {
        // This method is not used in integration tests - we use the sync Create method
        throw new NotImplementedException("Use Create(string accessToken, string scope) instead for testing.");
    }
}
