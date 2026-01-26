using System.Text.RegularExpressions;
using Hangfire;
using Microsoft.Extensions.Logging;
using YouTubester.Abstractions.Channels;
using YouTubester.Abstractions.Credits;
using YouTubester.Abstractions.Replies;
using YouTubester.Abstractions.Videos;
using YouTubester.Application.Credits;
using YouTubester.Domain;
using YouTubester.Integration;
using YouTubester.Integration.Exceptions;

namespace YouTubester.Application.Jobs;

public sealed class CommentScanJob(
    ILogger<CommentScanJob> logger,
    IBackgroundYoutubeIntegration youTubeIntegration,
    IAiClient aiClient,
    IVideoRepository videoRepository,
    IReplyRepository replyRepository,
    IChannelRepository channelRepository,
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

        var userId = channel.UserId;
        var drafted = 0;

        foreach (var video in await videoRepository.GetCommentableVideosAsync(channelId, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (video.Visibility != VideoVisibility.Public)
            {
                continue;
            }

            try
            {
                await foreach (var thread in youTubeIntegration.GetUnansweredTopLevelCommentsAsync(
                                   channelId, video.VideoId, cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Try to claim the comment first with Drafting status.
                    // If someone else already claimed it, skip without AI call.
                    var draftingReply = Reply.CreateDrafting(
                        thread.ParentCommentId,
                        thread.VideoId,
                        video.Title ?? string.Empty,
                        thread.Text,
                        dateTimeOffsetProvider.GetUtcNowDateTimeOffset());

                    var claimed = await replyRepository.TryClaimForDraftingAsync(draftingReply, cancellationToken);
                    if (!claimed)
                    {
                        // Already claimed by another process, skip
                        continue;
                    }

                    string replyText;
                    if (IsEmojiOnly(thread.Text))
                    {
                        replyText = "🔥🙌";
                    }
                    else
                    {
                        var idempotencyKey =
                            $"ai-reply-generated:{userId}:{thread.VideoId}:{thread.ParentCommentId}";

                        var spendSucceeded = await creditsService.TrySpendAsync(
                            userId,
                            CreditActionType.AiReplyGenerated.ToString(),
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
                                cancellationToken);
                        }
                        catch
                        {
                            var refundIdempotencyKey =
                                $"ai-reply-generated-refund:{userId}:{thread.VideoId}:{thread.ParentCommentId}";

                            await creditsService.RefundAsync(
                                userId,
                                CreditActionType.AiReplyGenerated.ToString(),
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