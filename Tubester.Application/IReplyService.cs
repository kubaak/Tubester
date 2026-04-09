using Tubester.Application.Contracts.Replies;
using Tubester.Domain;

namespace Tubester.Application;

public interface IReplyService
{
    Task<IEnumerable<Reply>> GetRepliesForApprovalAsync(CancellationToken cancellationToken);
    Task<Reply?> DeleteAsync(string commentId, CancellationToken cancellationToken);

    Task<BatchDecisionResultDto> ApplyBatchAsync(
        string userId,
        string operationId,
        IEnumerable<DraftDecisionDto> decisions,
        CancellationToken cancellationToken);

    Task<BatchIgnoreResult> IgnoreBatchAsync(string[] commentIds, CancellationToken ct);
}
