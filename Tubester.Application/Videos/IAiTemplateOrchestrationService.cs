using Tubester.Application.Contracts.Videos;

namespace Tubester.Application.Videos;

public interface IAiTemplateOrchestrationService
{
    Task EnqueueAiTemplateAsync(string userId, string operationId, AiVideoTemplateRequest request, CancellationToken ct);
}
