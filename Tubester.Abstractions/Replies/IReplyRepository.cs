using Pgvector;
using Tubester.Domain;

namespace Tubester.Abstractions.Replies;

public interface IReplyRepository
{
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

    /// <summary>
    /// Searches for relevant approved replies using vector similarity.
    /// Only returns replies with CommentEmbedding that match userId/channelId.
    /// </summary>
    Task<IReadOnlyList<RelevantReplyExample>> SearchRelevantReplyExamplesAsync(
        string channelId,
        Vector commentEmbedding,
        int limit,
        double minSimilarityScore,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets a page of replies with optional status filtering and cursor-based pagination for a specific channel.
    /// </summary>
    /// <param name="uploadPlaylistId">Upload playlist ID to filter replies by.</param>
    /// <param name="statuses">Optional set of statuses to include.</param>
    /// <param name="videoIds">Optional set of video IDs to include.</param>
    /// <param name="originalComment">Optional case-insensitive substring filter for original comment text.</param>
    /// <param name="afterPulledAtUtc">Cursor: original comment timestamp to search after (exclusive).</param>
    /// <param name="afterCommentId">Cursor: comment ID to search after when timestamps are equal.</param>
    /// <param name="take">Maximum number of items to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of replies ordered by OriginalCommentAt DESC, CommentId DESC.</returns>
    Task<List<Reply>> GetRepliesPageAsync(
        string uploadPlaylistId,
        IReadOnlyCollection<ReplyStatus>? statuses,
        IReadOnlyCollection<string>? videoIds,
        string? originalComment,
        DateTimeOffset? afterPulledAtUtc,
        string? afterCommentId,
        int take,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets replies missing comment embeddings for a specific upload playlist.
    /// Only returns replies with eligible statuses (Posted or Approved) and non-empty text fields.
    /// </summary>
    /// <param name="uploadPlaylistId">The uploads playlist ID to filter by.</param>
    /// <param name="take">Maximum number of replies to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of eligible replies ordered by OriginalCommentAt, CommentId.</returns>
    Task<IReadOnlyList<Reply>> GetRepliesMissingCommentEmbeddingAsync(
        string uploadPlaylistId,
        int take,
        CancellationToken cancellationToken);

    /// <summary>
    /// Saves changes to tracked entities.
    /// </summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
