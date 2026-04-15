using System.Text.RegularExpressions;
using Hangfire;
using Microsoft.Extensions.Logging;
using Tubester.Abstractions.Channels;
using Tubester.Abstractions.Credits;
using Tubester.Abstractions.Replies;
using Tubester.Abstractions.Videos;
using Tubester.Application.Credits;
using Tubester.Domain;
using Tubester.Integration;
using Tubester.Integration.Exceptions;

namespace Tubester.Application.Jobs;

public sealed class CommentScanJob(
    ILogger<CommentScanJob> logger,
    IBackgroundYoutubeIntegration youTubeIntegration,
    IAiClient aiClient,
    IVideoRepository videoRepository,
    IReplyRepository replyRepository,
    IChannelRepository channelRepository,
    IChannelSettingsRepository channelSettingsRepository,
    ICreditsService creditsService,
    IDateTimeOffsetProvider dateTimeOffsetProvider)
{
    [Queue("scanning")]
    [AutomaticRetry(Attempts = 3, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    public async Task Run(string channelId, IJobCancellationToken jobCancellationToken)
    {
        jobCancellationToken.ThrowIfCancellationRequested();

        try
        {
            var count = await ScanOnceAsync(channelId, jobCancellationToken.ShutdownToken);
            logger.LogInformation("Comment scan completed. Drafted: {Count}", count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Comment scan failed");
            throw;
        }
        finally
        {
            await channelRepository.ReleaseCommentScanLockAsync(channelId, CancellationToken.None);
        }
    }

    private static bool IsEmojiOnly(string text)
    {
        return !_nonEmojiRegex.IsMatch(text);
    }

    private async Task<int> ScanOnceAsync(string channelId, CancellationToken cancellationToken)
    {
        var channel = await channelRepository.GetChannelAsync(channelId, cancellationToken);
        if (channel is null)
        {
            logger.LogWarning("Channel {ChannelId} not found, skipping scan", channelId);
            return 0;
        }

        var settings = await channelSettingsRepository.GetByChannelIdAsync(channelId, cancellationToken);
        if (settings is null || !settings.IsCommentAssistantEnabled)
        {
            logger.LogInformation(
                "Skipping comment scan for channel {ChannelId}: comment assistant disabled", channelId);
            return 0;
        }

        var userId = channel.UserId;
        var drafted = 0;
        var nowUtc = dateTimeOffsetProvider.GetUtcNowDateTimeOffset();

        foreach (var video in await videoRepository.GetCommentableVideosAsync(channelId, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (video.Visibility != VideoVisibility.Public)
            {
                continue;
            }

            if (settings.MaxSuggestedRepliesPerSync > 0 && drafted >= settings.MaxSuggestedRepliesPerSync)
            {
                logger.LogInformation(
                    "Skipping further comments for channel {ChannelId}: max suggestions reached ({MaxSuggestions})",
                    channelId, settings.MaxSuggestedRepliesPerSync);
                break;
            }

            try
            {
                await foreach (var thread in youTubeIntegration.GetUnansweredTopLevelCommentsAsync(
                                   channelId, video.VideoId, cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (settings.MaxSuggestedRepliesPerSync > 0 && drafted >= settings.MaxSuggestedRepliesPerSync)
                    {
                        logger.LogInformation(
                            "Skipping further comments for channel {ChannelId}: max suggestions reached ({MaxSuggestions})",
                            channelId, settings.MaxSuggestedRepliesPerSync);
                        break;
                    }

                    if (settings.MaxCommentAgeDays > 0 && thread.PublishedAt.HasValue)
                    {
                        var commentAge = nowUtc - thread.PublishedAt.Value;
                        if (commentAge.TotalDays > settings.MaxCommentAgeDays)
                        {
                            logger.LogDebug(
                                "Skipping comment {CommentId} for channel {ChannelId}: comment not eligible because too old",
                                thread.ParentCommentId, channelId);
                            continue;
                        }
                    }

                    // Try to claim the comment first with Drafting status.
                    // If someone else already claimed it, skip without AI call.
                    var draftingReply = Reply.CreateDrafting(
                        thread.ParentCommentId,
                        thread.VideoId,
                        video.Title ?? string.Empty,
                        thread.Text,
                        dateTimeOffsetProvider.GetUtcNowDateTimeOffset(),
                        thread.PublishedAt ?? DateTimeOffset.MinValue);

                    var claimed = await replyRepository.TryClaimForDraftingAsync(draftingReply, cancellationToken);
                    if (!claimed)
                    {
                        // Already claimed by another process, skip
                        continue;
                    }

                    string replyText;
                    if (IsEmojiOnly(thread.Text))
                    {
                        replyText = !string.IsNullOrWhiteSpace(settings.ResponseForNonTextualComments)
                            ? settings.ResponseForNonTextualComments
                            : "🔥🙌";
                    }
                    else
                    {
                        var idempotencyKey =
                            $"ai-reply-generated:{userId}:{thread.VideoId}:{thread.ParentCommentId}";

                        var spendSucceeded = await creditsService.TrySpendAsync(
                            userId,
                            nameof(CreditActionType.AiReplyGenerated),
                            idempotencyKey,
                            thread.ParentCommentId,
                            new { videoId = thread.VideoId, commentId = thread.ParentCommentId },
                            cancellationToken);

                        if (!spendSucceeded)
                        {
                            logger.LogWarning(
                                "Insufficient credits to generate AI reply for comment {CommentId}, skipping",
                                thread.ParentCommentId);
                            return drafted;
                        }

                        string? suggestion;
                        try
                        {
                            suggestion = await aiClient.SuggestReplyAsync(
                                video.Title ?? string.Empty,
                                video.Tags,
                                thread.Text,
                                settings.ReplyLanguage,
                                cancellationToken);
                        }
                        catch
                        {
                            var refundIdempotencyKey =
                                $"ai-reply-generated-refund:{userId}:{thread.VideoId}:{thread.ParentCommentId}";

                            await creditsService.RefundAsync(
                                userId,
                                nameof(CreditActionType.AiReplyGenerated),
                                idempotencyKey,
                                refundIdempotencyKey,
                                cancellationToken);

                            throw;
                        }

                        replyText = string.IsNullOrWhiteSpace(suggestion)
                            ? "Thanks for the comment! 🙌"
                            : suggestion;
                    }

                    // Update the reply with suggested text (transitions from Drafting to Suggested)
                    draftingReply.SuggestText(replyText, dateTimeOffsetProvider.GetUtcNowDateTimeOffset());
                    await replyRepository.AddOrUpdateReplyAsync(draftingReply, cancellationToken);

                    drafted++;
                }
            }
            catch (CommentsDisabledException ex)
            {
                logger.LogInformation(
                    "Comments are disabled for video {VideoId}, marking as CommentsAllowed = false",
                    ex.VideoId);

                await videoRepository.MarkCommentsDisabledAsync(channelId, ex.VideoId, cancellationToken);
            }
        }

        return drafted;
    }

    private static readonly Regex _nonEmojiRegex = new(@"\p{L}|\p{N}", RegexOptions.Compiled);
}