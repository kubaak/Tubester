using Microsoft.Extensions.DependencyInjection;
using Tubester.Abstractions;
using Tubester.Abstractions.ApplicationConfiguration;

namespace Tubester.Integration;

public sealed class EmbeddingServiceFactory(
    IServiceProvider serviceProvider,
    IAiRuntimeOptionsService aiRuntimeOptionsService)
    : IEmbeddingServiceFactory
{
    public async Task<IEmbeddingService> GetServiceAsync(CancellationToken ct)
    {
        var provider = await aiRuntimeOptionsService.GetProviderAsync(ct);

        IEmbeddingService embeddingService = provider switch
        {
            AiProviders.Ollama => serviceProvider.GetRequiredService<OllamaEmbeddingService>(),
            AiProviders.Gemini => serviceProvider.GetRequiredService<GeminiEmbeddingService>(),
            _ => throw new InvalidOperationException(
                $"Unsupported AI provider for embeddings: {provider}. Supported providers: {string.Join(", ", AiProviders.All)}")
        };

        return embeddingService;
    }
}
