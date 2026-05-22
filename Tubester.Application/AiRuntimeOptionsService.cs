using Microsoft.Extensions.Logging;
using Tubester.Abstractions.ApplicationConfiguration;

namespace Tubester.Application;

public sealed class AiRuntimeOptionsService(
    IApplicationConfigurationService configService,
    ILogger<AiRuntimeOptionsService> logger)
    : IAiRuntimeOptionsService
{
    public async Task<AiRuntimeOptions> GetAsync(AiOperation operation, CancellationToken ct)
    {
        string? model = null;
        double? temperature = null;
        int? maxOutputTokens = null;
        int? numCtx = null;
        switch (operation)
        {
            case AiOperation.PlaylistSuggestion:
                model = await configService.GetValueAsync<string>(ApplicationConfigurationKeys.AiPlaylistModel, ct);
                temperature = await configService.GetValueAsync<double>(ApplicationConfigurationKeys.AiPlaylistTemperature, ct);
                maxOutputTokens = await configService.GetValueAsync<int>(ApplicationConfigurationKeys.AiPlaylistMaxOutputTokens, ct);
                numCtx = await configService.GetValueAsync<int>(ApplicationConfigurationKeys.AiPlaylistNumCtx, ct);
                break;
            case AiOperation.Metadata:
                model = await configService.GetValueAsync<string>(ApplicationConfigurationKeys.AiModel, ct);
                temperature = await configService.GetValueAsync<double>(ApplicationConfigurationKeys.AiTemperature, ct);
                maxOutputTokens = await configService.GetValueAsync<int>(ApplicationConfigurationKeys.AiDetailsMaxOutputTokens, ct);
                break;
            case AiOperation.Reply:
                model = await configService.GetValueAsync<string>(ApplicationConfigurationKeys.AiModel, ct);
                temperature = await configService.GetValueAsync<double>(ApplicationConfigurationKeys.AiTemperature, ct);
                maxOutputTokens = await configService.GetValueAsync<int>(ApplicationConfigurationKeys.AiReplyMaxOutputTokens, ct);
                break;
        }
        var provider = await configService.GetValueAsync<string>(ApplicationConfigurationKeys.AiProvider, ct);


        // Use defaults if not configured
        provider ??= AiProviders.Ollama;
        model ??= "qwen3:8b";
        temperature ??= 1.0;
        maxOutputTokens ??= 1000;
        numCtx ??= 0;

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

        return new AiRuntimeOptions(provider, model, temperature.Value, maxOutputTokens.Value, numCtx.Value);
    }

    public async Task<string> GetProviderAsync(CancellationToken ct)
    {
        var provider = await configService.GetValueAsync<string>(ApplicationConfigurationKeys.AiProvider, ct);
        return string.IsNullOrEmpty(provider) ? throw new InvalidOperationException("No provider configured.") : provider;
    }
}
