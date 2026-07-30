namespace Tubester.Abstractions.Observability;

/// <summary>
/// Records Tubester application metrics.
///
/// Important:
/// Do not pass high-cardinality values such as userId, channelId, videoId,
/// commentId, jobId, exception messages, or request IDs.
/// Use logs/traces for those values instead.
/// </summary>
public interface ITubesterMetrics
{
    void CommentScanStarted();
    void CommentScanSucceeded();
    void CommentScanFailed();

    void ReplyGenerationStarted();
    void ReplyGenerationSucceeded();
    void ReplyGenerationFailed();

    void EmbeddingGenerationStarted();
    void EmbeddingGenerationSucceeded();
    void EmbeddingGenerationFailed();

    void YouTubeApiCall(string operation);
    void YouTubeApiError(string operation);

    void AiCall(string operation);
    void AiCallFailed(string operation);
}