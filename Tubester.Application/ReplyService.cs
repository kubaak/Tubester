using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tubester.Abstractions;
using Tubester.Abstractions.Channels;
using Tubester.Abstractions.Credits;
using Tubester.Abstractions.Replies;
using Tubester.Application.Common;
using Tubester.Application.Contracts;
using Tubester.Application.Contracts.Replies;
using Tubester.Application.Credits;
using Tubester.Application.Exceptions;
using Tubester.Application.Options;
using Tubester.Domain;
using Tubester.Integration;

namespace Tubester.Application;

public class ReplyService(
    IReplyRepository repository,
    IYouTubeIntegration youTubeIntegration,
    ICurrentChannelContext currentChannelContext,
    ICreditsService creditsService,
    IDateTimeOffsetProvider dateTimeOffsetProvider,
    IOptions<ReplyListingOptions> replyListingOptions,
    ILogger<ReplyService> replyLogger)
    : IReplyService
{
    public async Task<PagedResult<ReplyListItemDto>> GetRepliesAsync(
        ReplyStatus[]? statuses,
        string[]? videoIds,
        string? originalComment,
        int? pageSize,
        string? pageToken,
        CancellationToken ct)
    {
        var options = replyListingOptions.Value;
        var effectivePageSize = pageSize ?? options.DefaultPageSize;
        if (effectivePageSize < 1 || effectivePageSize > options.MaxPageSize)
        {
            throw new InvalidPageSizeException(effectivePageSize, options.MaxPageSize);
        }

        var normalizedOriginalComment = string.IsNullOrWhiteSpace(originalComment)
            ? null
            : originalComment.Trim();

        var normalizedStatuses = statuses?
            .Distinct()
            .ToArray();

        if (normalizedStatuses is { Length: 0 })
        {
            normalizedStatuses = null;
        }

        var normalizedVideoIds = videoIds?
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalizedVideoIds is { Length: 0 })
        {
            normalizedVideoIds = null;
        }

        var statusBinding = normalizedStatuses is null
            ? string.Empty
            : string.Join(',', normalizedStatuses.Select(s => s.ToString()));

        var videoIdsBinding = normalizedVideoIds is null
            ? null
            : string.Join(',', normalizedVideoIds);

        var binding = (string.IsNullOrEmpty(statusBinding), videoIdsBinding) switch
        {
            (true, null) => string.Empty,
            (true, not null) => videoIdsBinding,
            (false, null) => statusBinding,
            (false, not null) => $"{statusBinding}|{videoIdsBinding}"
        };

        DateTimeOffset? afterOriginalCommentAtUtc = null;
        string? afterCommentId = null;

        if (!string.IsNullOrWhiteSpace(pageToken))
        {
            if (!PageToken.TryParse(pageToken, out var originalCommentAt, out var commentId, out var tokenBinding))
            {
                replyLogger.LogWarning("Invalid page token received for replies listing");
                throw new InvalidPageTokenException();
            }

            if (!string.Equals(tokenBinding, binding, StringComparison.Ordinal))
            {
                replyLogger.LogWarning(
                    "Page token binding mismatch for replies listing. Expected '{ExpectedBinding}', got '{ActualBinding}'",
                    binding,
                    tokenBinding);

                throw new InvalidPageTokenException("Page token does not match current filters.");
            }

            afterOriginalCommentAtUtc = originalCommentAt;
            afterCommentId = commentId;
        }

        var uploadPlaylistId = currentChannelContext.GetRequiredUploadPlaylistId();
        var take = effectivePageSize + 1;

        var replies = await repository.GetRepliesPageAsync(
            uploadPlaylistId,
            normalizedStatuses,
            normalizedVideoIds,
            normalizedOriginalComment,
            afterOriginalCommentAtUtc,
            afterCommentId,
            take,
            ct);

        var hasMore = replies.Count > effectivePageSize;
        var itemsToReturn = hasMore
            ? replies.Take(effectivePageSize).ToList()
            : replies;

        string? nextPageToken = null;
        if (hasMore && itemsToReturn.Count > 0)
        {
            var lastItem = itemsToReturn[^1];
            nextPageToken = PageToken.Serialize(lastItem.OriginalCommentAt, lastItem.CommentId, binding);
        }

        var items = itemsToReturn.Select(reply => new ReplyListItemDto
        {
            CommentId = reply.CommentId,
            VideoId = reply.VideoId,
            VideoTitle = reply.VideoTitle,
            CommentText = reply.CommentText,
            ThumbnailUrl = reply.ThumbnailUrl,
            OriginalCommentAt = reply.OriginalCommentAt,
            SuggestedText = reply.SuggestedText
        }).ToList();

        return new PagedResult<ReplyListItemDto>
        {
            Items = items,
            NextPageToken = nextPageToken
        };
    }

    public async Task<Reply?> DeleteAsync(string commentId, CancellationToken cancellationToken)
    {
        return await repository.DeleteReplyAsync(commentId, cancellationToken);
    }

    public async Task<BatchIgnoreResult> IgnoreBatchAsync(string[] commentIds, CancellationToken ct)
    {
        var ids = commentIds.Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (ids.Length == 0)
        {
            return new BatchIgnoreResult(0, 0, 0, 0, 0, [], [], [], []);
        }

        var matches = await repository.LoadStatusesAsync(ids, ct);
        var matchedSet = matches.Select(m => m.CommentId).ToHashSet(StringComparer.Ordinal);

        var notFound = ids.Where(id => !matchedSet.Contains(id)).ToArray();
        var alreadyIgnored = matches.Where(m => m.Status == ReplyStatus.Ignored).Select(m => m.CommentId).ToArray();
        var skippedPosted = matches.Where(m => m.Status == ReplyStatus.Posted).Select(m => m.CommentId).ToArray();

        var toIgnore = matches
            .Where(m => m.Status != ReplyStatus.Posted && m.Status != ReplyStatus.Ignored)
            .Select(m => m.CommentId)
            .ToArray();

        var actuallyIgnored = toIgnore.Length == 0
            ? []
            : await repository.IgnoreManyAsync(toIgnore, ct);

        return new BatchIgnoreResult(
            ids.Length,
            actuallyIgnored.Length,
            alreadyIgnored.Length,
            skippedPosted.Length,
            notFound.Length,
            actuallyIgnored,
            alreadyIgnored,
            skippedPosted,
            notFound
        );
    }

    public async Task<BatchDecisionResultDto> ApplyBatchAsync(
        string userId,
        string operationId,
        IEnumerable<DraftDecisionDto> decisions,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("User id is required.", nameof(userId));
        }

        var results = new List<DraftDecisionResultDto>();
        int ok = 0, fail = 0;

        foreach (var d in decisions)
        {
            try
            {
                var draft = await repository.GetReplyAsync(d.CommentId, cancellationToken);
                if (draft is null)
                {
                    results.Add(new DraftDecisionResultDto(d.CommentId, false, "Draft not found"));
                    fail++;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(d.ApprovedText))
                {
                    results.Add(new DraftDecisionResultDto(d.CommentId, false, "Draft is empty"));
                    fail++;
                    continue;
                }

                draft.ApproveText(userId, d.ApprovedText, dateTimeOffsetProvider.GetUtcNowDateTimeOffset());

                var replyPostedIdempotencyKey =
                    $"reply-posted:{userId}:{draft.VideoId}:{draft.CommentId}:{operationId}";

                var spendResult = await creditsService.TrySpendAsync(
                    userId,
                    nameof(CreditActionType.ReplyPostedToYouTube),
                    replyPostedIdempotencyKey,
                    draft.CommentId,
                    new
                    {
                        length = d.ApprovedText.Length,
                        wasEdited = !string.Equals(d.ApprovedText, draft.SuggestedText, StringComparison.Ordinal)
                    },
                    cancellationToken);

                if (!spendResult.Succeeded)
                {
                    results.Add(new DraftDecisionResultDto(d.CommentId, false, "Insufficient credits."));
                    fail++;
                    continue;
                }

                await youTubeIntegration.ReplyAsync(draft.CommentId, draft.FinalText!, cancellationToken);
                draft.Post(userId, dateTimeOffsetProvider.GetUtcNowDateTimeOffset());

                await repository.AddOrUpdateReplyAsync(draft, cancellationToken);
                results.Add(new DraftDecisionResultDto(d.CommentId, true));
                ok++;
            }
            catch (Exception ex)
            {
                results.Add(new DraftDecisionResultDto(d.CommentId, false, ex.Message));
                fail++;
            }
        }

        return new BatchDecisionResultDto(
            ok + fail,
            ok,
            fail,
            results);
    }
}