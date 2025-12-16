using YouTubester.Domain;

namespace YouTubester.Abstractions.Replies;

public interface IReplyRepository
{
    Task<IEnumerable<Reply>> GetRepliesForApprovalAsync(string channelId, CancellationToken cancellationToken);

    Task<Reply?> GetReplyAsync(string commentId, CancellationToken cancellationToken);

    Task AddOrUpdateReplyAsync(Reply reply, CancellationToken cancellationToken);

    Task<Reply?> DeleteReplyAsync(string commentId, CancellationToken cancellationToken);

    Task<List<(string CommentId, ReplyStatus Status)>> LoadStatusesAsync(
        IEnumerable<string> ids,
        CancellationToken cancellationToken);

    Task<string[]> IgnoreManyAsync(IEnumerable<string> ids, CancellationToken cancellationToken);
}