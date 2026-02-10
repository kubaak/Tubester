using Tubester.Application.Contracts.Videos;

namespace Tubester.Application.Videos;

public interface IAiVideoTemplatingService
{
    Task GenerateAiTemplateAsync(
        string channelId,
        AiVideoTemplateRequest request,
        CancellationToken cancellationToken);
}