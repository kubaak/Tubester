using YouTubester.Domain;

namespace YouTubester.Abstractions.Replies;

public interface IReplyRepository
{
    Task<IEnumerable<Reply>> GetRepliesForApprovalAsync(string channelId, CancellationToken cancellationToken);

    Task<Reply?> GetReplyAsync(string commentId, CancellationToken cancellationToken);

    /// <summary>
    /// Attempts to claim a comment for drafting by inserting a record with Drafting status.
    /// Uses INSERT ... ON CONFLICT DO NOTHING to handle race conditions.
    /// </summary>
    /// <returns>True if the claim succeeded (row was inserted), false if already claimed.</returns>
    Task<bool> TryClaimForDraftingAsync(Reply reply, CancellationToken cancellationToken);

    Task AddOrUpdateReplyAsync(Reply reply, CancellationToken cancellationToken);

    Task<Reply?> DeleteReplyAsync(string commentId, CancellationToken cancellationToken);

    Task<List<(string CommentId, ReplyStatus Status)>> LoadStatusesAsync(
        IEnumerable<string> ids,
        CancellationToken cancellationToken);

    Task<string[]> IgnoreManyAsync(IEnumerable<string> ids, CancellationToken cancellationToken);
}