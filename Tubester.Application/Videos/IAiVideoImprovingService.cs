using Tubester.Application.Contracts.Videos;

namespace Tubester.Application.Videos;

public interface IAiVideoImprovingService
{
    Task GenerateAiTemplateAsync(
        string channelId,
        AiVideoTemplateRequest request,
        CancellationToken cancellationToken);
    Task SuggestPlaylistIdsAsync(
        string channelId,
        string videoId,
        string context,
        CancellationToken cancellationToken);
}