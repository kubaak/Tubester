namespace Tubester.Abstractions.Observability;

/// <summary>
/// Prometheus-friendly metric names for Tubester.
/// </summary>
public static class MetricNames
{
    public const string MeterName = "Tubester";

    // Comment scan metrics
    public const string CommentScanStarted = "tubester_comment_scan_started_total";
    public const string CommentScanSucceeded = "tubester_comment_scan_succeeded_total";
    public const string CommentScanFailed = "tubester_comment_scan_failed_total";

    // Reply generation metrics
    public const string ReplyGenerationStarted = "tubester_reply_generation_started_total";
    public const string ReplyGenerationSucceeded = "tubester_reply_generation_succeeded_total";
    public const string ReplyGenerationFailed = "tubester_reply_generation_failed_total";

    // Embedding generation metrics
    public const string EmbeddingGenerationStarted = "tubester_embedding_generation_started_total";
    public const string EmbeddingGenerationSucceeded = "tubester_embedding_generation_succeeded_total";
    public const string EmbeddingGenerationFailed = "tubester_embedding_generation_failed_total";

    // YouTube API metrics
    public const string YouTubeApiCalls = "tubester_youtube_api_calls_total";
    public const string YouTubeApiErrors = "tubester_youtube_api_errors_total";

    // AI metrics
    public const string AiCalls = "tubester_ai_calls_total";
    public const string AiCallErrors = "tubester_ai_call_errors_total";

    // Business gauges
    public const string UsersTotal = "tubester_users_total";
    public const string ChannelsTotal = "tubester_channels_total";
    public const string VideosTotal = "tubester_videos_total";
    public const string RepliesTotal = "tubester_replies_total";
    public const string AccountsWithWritePermissionTotal = "tubester_accounts_with_write_permission_total";
}