using Hangfire;
using YouTubester.Abstractions.Channels;
using YouTubester.Abstractions.Videos;
using YouTubester.Application.Contracts.Videos;
using YouTubester.Application.Exceptions;
using YouTubester.Application.Jobs;

namespace YouTubester.Application.Videos;

public class AiTemplateOrchestrationService(
    ICurrentChannelContext channelContext,
    IVideoRepository videoRepository,
    IBackgroundJobClient backgroundJobClient) : IAiTemplateOrchestrationService
{
    public async Task<string> EnqueueAiTemplateAsync(AiVideoTemplateRequest request,
        CancellationToken cancellationToken)
    {
        var channelId = channelContext.GetRequiredChannelId();
        var isMarked = await videoRepository.TrySettingAiTemplateInProgressAsync(channelId, request.TargetVideoId, true,
            cancellationToken);

        if (!isMarked)
        {
            throw new AiTemplatingNotStartedException(
                $"Target video {request.TargetVideoId} not found for current channel or an AI template job is already in progress.");
        }

        try
        {
            var jobId = backgroundJobClient.Enqueue<AiTemplateJob>(
                job => job.Run(channelId, request, JobCancellationToken.Null));

            backgroundJobClient.ContinueJobWith<AiTemplateFinalizeJob>(
                jobId,
                job => job.Run(channelId, request.TargetVideoId, JobCancellationToken.Null),
                JobContinuationOptions.OnAnyFinishedState);

            return jobId;
        }
        catch
        {
            await videoRepository.TrySettingAiTemplateInProgressAsync(channelId, request.TargetVideoId, false,
                cancellationToken);
            throw;
        }
    }
}