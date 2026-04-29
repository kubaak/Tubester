using Microsoft.Extensions.Logging;
using Tubester.Abstractions.ApplicationConfiguration;

namespace Tubester.Application;

public sealed class AiRuntimeOptionsService(
    IApplicationConfigurationService configService,
    ILogger<AiRuntimeOptionsService> logger)
    : IAiRuntimeOptionsService
{
    public async Task<AiRuntimeOptions> GetAsync(CancellationToken ct)
    {
        var provider = await configService.GetValueAsync<string>(ApplicationConfigurationKeys.AiProvider, ct);
        var model = await configService.GetValueAsync<string>(ApplicationConfigurationKeys.AiModel, ct);

        // Use defaults if not configured
        provider ??= AiProviders.Ollama;
        model ??= "qwen3:8b";

        // Validate provider
        if (!AiProviders.All.Contains(provider, StringComparer.OrdinalIgnoreCase))
        {
            var message = $"Unsupported AI provider '{provider}'. Supported providers: {string.Join(", ", AiProviders.All)}.";
            logger.LogError("Invalid AI provider configuration: {Message}", message);
            throw new InvalidOperationException(message);
        }

        logger.LogDebug(
            "Resolved AI runtime options. Provider: {Provider}, Model: {Model}",
            provider,
            model);

        return new AiRuntimeOptions(provider, model);
    }
}
