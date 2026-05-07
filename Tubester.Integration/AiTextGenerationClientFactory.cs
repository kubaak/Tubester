using Microsoft.Extensions.DependencyInjection;
using Tubester.Abstractions.ApplicationConfiguration;

namespace Tubester.Integration;

public sealed class AiTextGenerationClientFactory(
    IServiceProvider serviceProvider,
    IAiRuntimeOptionsService aiRuntimeOptionsService)
    : IAiTextGenerationClientFactory
{
    public async Task<IAiTextGenerationClient> GetClientAsync(CancellationToken cancellationToken)
    {
        var provider = await aiRuntimeOptionsService.GetProviderAsync(cancellationToken);

        IAiTextGenerationClient textGenerationClient = provider switch
        {
            AiProviders.Ollama => serviceProvider.GetRequiredService<OllamaTextGenerationClient>(),
            AiProviders.Gemini => serviceProvider.GetRequiredService<GeminiTextGenerationClient>(),
            _ => throw new InvalidOperationException(
                $"Unsupported AI provider: {provider}. Supported providers: {string.Join(", ", AiProviders.All)}")
        };

        return textGenerationClient;
    }
}
