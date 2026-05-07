using Hangfire;
using Tubester.Abstractions.Analytics;
using Tubester.Abstractions.Channels;
using Tubester.Abstractions.Credits;
using Tubester.Abstractions.Videos;
using Tubester.Application.Common;
using Tubester.Application.Contracts.Videos;
using Tubester.Application.Credits;
using Tubester.Application.Jobs;

namespace Tubester.Application.Videos;

public class AiTemplateOrchestrationService(
    ICurrentChannelContext channelContext,
    IVideoRepository videoRepository,
    IBackgroundJobClient backgroundJobClient,
    IUserEventLogger userEventLogger,
    ICreditsService creditsService) : IAiTemplateOrchestrationService
{
    public async Task EnqueueAiTemplateAsync(
        string userId,
        string operationId,
        AiVideoTemplateRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);

        var shouldGenerateMetadata =
            request.GenerateTitle || request.GenerateDescription || request.GenerateTags;

        var shouldSuggestPlaylists = request.SuggestPlaylists;

        string creditAction;
        string keyBase;
        switch (shouldGenerateMetadata)
        {
            case true when shouldSuggestPlaylists:
                creditAction = nameof(CreditActionType.AiTemplateWithPlaylistEnqueued);
                keyBase = "ai-template-playlist-enqueue";
                break;
            case true:
                creditAction = nameof(CreditActionType.AiTemplateEnqueued);
                keyBase = "ai-template-enqueue";
                break;
            default:
                {
                    if (shouldSuggestPlaylists)
                    {
                        creditAction = nameof(CreditActionType.AiPlaylistSuggestionEnqueued);
                        keyBase = "ai-playlist-enqueue";
                    }
                    else
                    {
                        throw new BadRequestException("At least one AI template action must be requested.");
                    }

                    break;
                }
        }

        var uploadPlaylistId = channelContext.GetRequiredUploadPlaylistId();
        var channelId = channelContext.GetRequiredChannelId();

        var operations = request.GetRequestedAiOperations();

        var marked = false;

        try
        {
            var aiTemplateEnqueueIdempotencyKey =
                $"{keyBase}:{userId}:{request.TargetVideoId}:{operationId}";

            var spendResult = await creditsService.TrySpendAsync(
                userId,
                creditAction,
                aiTemplateEnqueueIdempotencyKey,
                request.TargetVideoId,
                new
                {
                    generateTitle = request.GenerateTitle,
                    generateDescription = request.GenerateDescription,
                    generateTags = request.GenerateTags,
                    suggestPlaylists = request.SuggestPlaylists
                },
                cancellationToken);

            if (!spendResult.Succeeded)
            {
                throw new PaymentRequiredException("Insufficient credits to enqueue AI templating.");
            }

            if (spendResult.WasDuplicate)
            {
                return;
            }

            marked = await videoRepository.TryAddAiOperationsInProgressAsync(
                uploadPlaylistId,
                request.TargetVideoId,
                operations,
                cancellationToken);

            if (!marked)
            {
                throw new ConflictException(
                    $"Target video {request.TargetVideoId} not found for current channel or one of the requested AI operations is already in progress.");
            }

            string? detailsJobId = null;
            string? playlistSuggestionJobId = null;

            if (shouldGenerateMetadata)
            {
                var detailsRequest = new AiVideoDetailsRequest(
                    channelId,
                    uploadPlaylistId,
                    request.TargetVideoId,
                    request.PromptEnrichment,
                    request.GenerateTitle,
                    request.GenerateDescription,
                    request.GenerateTags,
                    request.SuggestPlaylists
                );
                detailsJobId = backgroundJobClient.Enqueue<AiTemplateJob>(
                    job => job.Run(detailsRequest, JobCancellationToken.Null));

                await userEventLogger.LogAsync(
                    userId,
                    UserEventType.AiTemplateEnqueued,
                    request.TargetVideoId,
                    null,
                    new
                    {
                        generateTitle = request.GenerateTitle,
                        generateDescription = request.GenerateDescription,
                        generateTags = request.GenerateTags
                    },
                    cancellationToken);

                backgroundJobClient.ContinueJobWith<AiTemplateFinalizeJob>(
                    detailsJobId,
                    job => job.Run(uploadPlaylistId, request, JobCancellationToken.Null),
                    JobContinuationOptions.OnAnyFinishedState);
            }

            if (shouldSuggestPlaylists)
            {
                var playlistSuggestionRequest = new PlaylistSuggestionRequest(channelId, uploadPlaylistId, request.TargetVideoId, request.PromptEnrichment);
                playlistSuggestionJobId = backgroundJobClient.Enqueue<AiPlaylistSuggestionJob>(
                    job => job.Run(playlistSuggestionRequest, JobCancellationToken.Null));

                await userEventLogger.LogAsync(
                    userId,
                    UserEventType.AiPlaylistSuggestionEnqueued,
                    request.TargetVideoId,
                    null,
                    new
                    {
                        suggestPlaylists = true,
                        hasPromptEnrichment = !string.IsNullOrWhiteSpace(request.PromptEnrichment),
                        generateTitle = request.GenerateTitle,
                        generateDescription = request.GenerateDescription,
                        generateTags = request.GenerateTags
                    },
                    cancellationToken);

                backgroundJobClient.ContinueJobWith<AiPlaylistSuggestionFinalizeJob>(
                    playlistSuggestionJobId,
                    job => job.Run(uploadPlaylistId, request.TargetVideoId, JobCancellationToken.Null),
                    JobContinuationOptions.OnAnyFinishedState);
            }
        }
        catch
        {
            if (marked)
            {
                await videoRepository.TryClearAiOperationsInProgressAsync(
                    uploadPlaylistId,
                    request.TargetVideoId,
                    operations,
                    cancellationToken);
            }

            throw;
        }
    }
}