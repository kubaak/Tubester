using Hangfire;
using Tubester.Application.Videos;

namespace Tubester.Application.Jobs;

public sealed class AiPlaylistSuggestionJob(
    IAiVideoImprovingService aiVideoImprovingService)
{
    [Queue("ai-playlist-suggestion")]
    [AutomaticRetry(Attempts = 3, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    public async Task Run(
        string channelId,
        string targetVideoId,
        string promptEnrichment,
        IJobCancellationToken jobCancellationToken)
    {
        jobCancellationToken.ThrowIfCancellationRequested();

        await aiVideoImprovingService.SuggestPlaylistIdsAsync(channelId, targetVideoId, promptEnrichment,
            jobCancellationToken.ShutdownToken);
    }
}
