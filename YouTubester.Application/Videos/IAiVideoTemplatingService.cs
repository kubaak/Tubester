using YouTubester.Application.Contracts.Videos;

namespace YouTubester.Application.Videos;

public interface IAiVideoTemplatingService
{
    Task GenerateAiTemplateAsync(
        string channelId,
        AiVideoTemplateRequest request,
        CancellationToken cancellationToken);
}