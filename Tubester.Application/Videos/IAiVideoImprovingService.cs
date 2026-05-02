using Tubester.Application.Jobs;

namespace Tubester.Application.Videos;

public interface IAiVideoImprovingService
{
    Task GenerateAiTemplateAsync(
        AiVideoDetailsRequest request,
        CancellationToken cancellationToken);
    Task SuggestPlaylistIdsAsync(
        PlaylistSuggestionRequest request,
        CancellationToken cancellationToken);
}