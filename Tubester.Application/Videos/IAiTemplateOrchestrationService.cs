using Tubester.Application.Contracts.Videos;

namespace Tubester.Application.Videos;

public interface IAiTemplateOrchestrationService
{
    Task<string> EnqueueAiTemplateAsync(string userId, string operationId, AiVideoTemplateRequest request, CancellationToken ct);
}
