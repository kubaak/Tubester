using Microsoft.Extensions.Logging;
using Tubester.Abstractions.ApplicationConfiguration;

namespace Tubester.Integration;

public sealed class AiClientFactory(
    IEnumerable<IAiClient> clients,
    IAiRuntimeOptionsService runtimeOptionsService,
    ILogger<AiClientFactory> logger)
    : IAiClientFactory
{
    public async Task<IAiClient> GetClientAsync(CancellationToken ct)
    {
        var options = await runtimeOptionsService.GetAsync(ct);

        var client = clients.FirstOrDefault(
            c => string.Equals(c.Provider, options.Provider, StringComparison.OrdinalIgnoreCase));

        if (client is null)
        {
            var message = $"No AI client registered for provider '{options.Provider}'. " +
                         $"Available providers: {string.Join(", ", clients.Select(c => c.Provider))}.";
            logger.LogError("Failed to resolve AI client: {Message}", message);
            throw new InvalidOperationException(message);
        }

        logger.LogDebug(
            "Resolved AI client for provider {Provider}",
            options.Provider);

        return client;
    }
}
