using YouTubester.Application.Contracts.Videos;

namespace YouTubester.Application.Videos;

public interface IAiTemplateOrchestrationService
{
    Task<string> EnqueueAiTemplateAsync(AiVideoTemplateRequest request, CancellationToken ct);
}