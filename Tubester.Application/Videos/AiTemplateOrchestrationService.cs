using Hangfire;
using Tubester.Abstractions.Analytics;
using Tubester.Abstractions.Channels;
using Tubester.Abstractions.Credits;
using Tubester.Abstractions.Transactions;
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
    ICreditsService creditsService,
    IApplicationTransactionRunner transactionRunner) : IAiTemplateOrchestrationService
{
    public async Task EnqueueAiTemplateAsync(
        string userId,
        string operationId,
        AiVideoTemplateRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);

        var shouldGenerateMetadata = false;
        var actions = new List<BatchSpendAction>();

        if (request.GenerateTitle)
        {
            shouldGenerateMetadata = true;
            actions.Add(new BatchSpendAction(
                nameof(CreditActionType.AiTitleEnqueued),
                $"ai-title-enqueue:{userId}:{request.TargetVideoId}:{operationId}",
                request.TargetVideoId,
                new { operationId }));
        }

        if (request.GenerateDescription)
        {
            shouldGenerateMetadata = true;
            actions.Add(new BatchSpendAction(
                nameof(CreditActionType.AiDescriptionEnqueued),
                $"ai-description-enqueue:{userId}:{request.TargetVideoId}:{operationId}",
                request.TargetVideoId,
                new { operationId }));
        }

        if (request.GenerateTags)
        {
            shouldGenerateMetadata = true;
            actions.Add(new BatchSpendAction(
                nameof(CreditActionType.AiTagsEnqueued),
                $"ai-tags-enqueue:{userId}:{request.TargetVideoId}:{operationId}",
                request.TargetVideoId,
                new { operationId }));
        }

        if (request.SuggestPlaylists)
        {
            actions.Add(new BatchSpendAction(
                nameof(CreditActionType.AiPlaylistSuggestionEnqueued),
                $"ai-playlist-enqueue:{userId}:{request.TargetVideoId}:{operationId}",
                request.TargetVideoId,
                new { operationId }));
        }

        if (actions.Count == 0)
        {
            throw new BadRequestException("At least one AI template action must be requested.");
        }

        var uploadPlaylistId = channelContext.GetRequiredUploadPlaylistId();
        var channelId = channelContext.GetRequiredChannelId();

        var operations = request.GetRequestedAiOperations();

        await transactionRunner.ExecuteAsync(async ct =>
        {
            var batchRequest = new BatchSpendRequest
            {
                UserId = userId,
                Actions = actions
            };

            var spendResult = await creditsService.TrySpendBatchAsync(batchRequest, ct);

            if (!spendResult.Succeeded)
            {
                throw new PaymentRequiredException("Insufficient credits to enqueue AI templating.");
            }

            if (request.ExpectedCreditCost != spendResult.FinalCost)
            {
                throw new ConflictException(
                    $"Expected credit cost {request.ExpectedCreditCost} does not match actual cost {spendResult.FinalCost}.");
            }

            if (spendResult.WasDuplicate)
            {
                return;
            }

            var marked = await videoRepository.TryAddAiOperationsInProgressAsync(
                uploadPlaylistId,
                request.TargetVideoId,
                operations,
                ct);

            if (!marked)
            {
                throw new ConflictException(
                    $"Target video {request.TargetVideoId} not found for current channel or one of the requested AI operations is already in progress.");
            }

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
                    request.SuggestPlaylists);

                var detailsJobId = backgroundJobClient.Enqueue<AiTemplateJob>(
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
                    ct);

                backgroundJobClient.ContinueJobWith<AiTemplateFinalizeJob>(
                    detailsJobId,
                    job => job.Run(uploadPlaylistId, request, JobCancellationToken.Null),
                    JobContinuationOptions.OnAnyFinishedState);
            }

            if (request.SuggestPlaylists)
            {
                var playlistSuggestionRequest = new PlaylistSuggestionRequest(
                    channelId,
                    uploadPlaylistId,
                    request.TargetVideoId,
                    request.PromptEnrichment);

                var playlistSuggestionJobId = backgroundJobClient.Enqueue<AiPlaylistSuggestionJob>(
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
                    ct);

                backgroundJobClient.ContinueJobWith<AiPlaylistSuggestionFinalizeJob>(
                    playlistSuggestionJobId,
                    job => job.Run(uploadPlaylistId, request.TargetVideoId, JobCancellationToken.Null),
                    JobContinuationOptions.OnAnyFinishedState);
            }
        }, cancellationToken);
    }
}
