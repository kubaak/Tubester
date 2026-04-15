using Tubester.Application.Contracts;
using Tubester.Application.Contracts.Replies;
using Tubester.Domain;

namespace Tubester.Application;

public interface IReplyService
{
    /// <summary>
    /// Gets a paginated list of replies with optional status filtering.
    /// </summary>
    /// <param name="statuses">Optional array of status filters.</param>
    /// <param name="videoIds">Optional array of video ID filters.</param>
    /// <param name="originalComment">Optional case-insensitive substring filter for original comment text.</param>
    /// <param name="pageSize">Number of items per page (1-100, defaults to configured DefaultPageSize).</param>
    /// <param name="pageToken">Cursor token for pagination, or null for first page.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Paginated result containing replies and optional next page token.</returns>
    /// <exception cref="Exceptions.InvalidPageSizeException">When pageSize is outside valid range.</exception>
    /// <exception cref="Exceptions.InvalidPageTokenException">When pageToken is malformed.</exception>
    Task<PagedResult<ReplyListItemDto>> GetRepliesAsync(
        ReplyStatus[]? statuses,
        string[]? videoIds,
        string? originalComment,
        int? pageSize,
        string? pageToken,
        CancellationToken ct);

    Task<Reply?> DeleteAsync(string commentId, CancellationToken cancellationToken);

    Task<BatchDecisionResultDto> ApplyBatchAsync(
        string userId,
        string operationId,
        IEnumerable<DraftDecisionDto> decisions,
        CancellationToken cancellationToken);

    Task<BatchIgnoreResult> IgnoreBatchAsync(string[] commentIds, CancellationToken ct);
}
