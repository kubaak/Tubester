using Google.Apis.Http;

namespace Tubester.IntegrationTests.TestHost;

/// <summary>
/// A fake HttpClientFactory that returns a configurable HttpMessageHandler.
/// Used to inject fake HTTP responses into Google API clients during testing.
/// </summary>
public sealed class FakeGoogleHttpClientFactory(HttpMessageHandler handler) : HttpClientFactory
{
    protected override HttpMessageHandler CreateHandler(CreateHttpClientArgs args)
    {
        return handler;
    }
}