using Google.GenAI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tubester.Integration;

public sealed class GeminiClientFactory(
    IOptions<GeminiOptions> options,
    ILogger<GeminiClientFactory> logger)
    : IGeminiClientFactory
{
    private readonly Lazy<Client> _client = new(() =>
    {
        var apiKey = options.Value.ApiKey;

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("Gemini API key is not configured.");
        }

        logger.LogInformation("Creating Google GenAI Gemini client");

        return new Client(apiKey: apiKey);
    });

    public Client CreateClient() => _client.Value;
}