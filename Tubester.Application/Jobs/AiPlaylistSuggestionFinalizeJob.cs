using Hangfire;
using Microsoft.Extensions.Logging;
using Tubester.Abstractions.Videos;
using Tubester.Domain;

namespace Tubester.Application.Jobs;

public sealed class AiPlaylistSuggestionFinalizeJob(
    ILogger<AiPlaylistSuggestionFinalizeJob> logger,
    IVideoRepository videoRepository)
{
    [Queue("ai-templating")]
    [AutomaticRetry(Attempts = 3, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    public async Task Run(
        string channelId,
        string videoId,
        IJobCancellationToken jobCancellationToken)
    {
        jobCancellationToken.ThrowIfCancellationRequested();

        try
        {
            await videoRepository.TryClearAiOperationsInProgressAsync(channelId, videoId, AiVideoOperationFlags.PlaylistSuggestion,
                jobCancellationToken.ShutdownToken);
            logger.LogInformation("AI playlist suggestion finalized for video {VideoId}", videoId);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "AI playlist suggestion failed for video {VideoId}", videoId);
            throw;
        }
    }
}