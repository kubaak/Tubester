using Tubester.Application.Contracts.Videos;

namespace Tubester.Application.Videos;

public interface IAiTemplateOrchestrationService
{
    Task<AiTemplateEnqueueResult> EnqueueAiTemplateAsync(string userId, string operationId, AiVideoTemplateRequest request, CancellationToken ct);
}
