using Hangfire;
using Tubester.Application.Videos;

namespace Tubester.Application.Jobs;

public sealed class AiPlaylistSuggestionJob(
    IAiVideoImprovingService aiVideoImprovingService)
{
    [Queue("ai-playlist-suggestion")]
    [AutomaticRetry(Attempts = 3, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    public async Task Run(
        PlaylistSuggestionRequest request,
        IJobCancellationToken jobCancellationToken)
    {
        jobCancellationToken.ThrowIfCancellationRequested();

        await aiVideoImprovingService.SuggestPlaylistIdsAsync(request, jobCancellationToken.ShutdownToken);
    }
}

public record PlaylistSuggestionRequest(
    string ChannelId,
    string UploadPlaylistId,
    string TargetVideoId,
    string PromptEnrichment);
