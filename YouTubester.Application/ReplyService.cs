using Hangfire;
using YouTubester.Abstractions.Replies;
using YouTubester.Application.Contracts.Replies;
using YouTubester.Application.Jobs;
using YouTubester.Domain;
using YouTubester.Integration;

namespace YouTubester.Application;

public class ReplyService(
    IReplyRepository repository,
    IBackgroundJobClient backgroundJobClient,
    IYouTubeIntegration youTubeIntegration)
    : IReplyService
{
    public Task<IEnumerable<Reply>> GetRepliesForApprovalAsync(CancellationToken cancellationToken)
    {
        return repository.GetRepliesForApprovalAsync(cancellationToken);
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

                draft.ApproveText(d.ApprovedText, DateTimeOffset.Now);

                await youTubeIntegration.ReplyAsync(draft.CommentId, draft.FinalText!, cancellationToken);
                draft.Post(DateTimeOffset.Now);

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