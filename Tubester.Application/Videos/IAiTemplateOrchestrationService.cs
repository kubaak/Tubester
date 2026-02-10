using Tubester.Application.Contracts.Videos;

namespace Tubester.Application.Videos;

public interface IAiTemplateOrchestrationService
{
    Task<string> EnqueueAiTemplateAsync(string userId, AiVideoTemplateRequest request, CancellationToken ct);
}
