using System.Diagnostics.Metrics;
using Tubester.Abstractions.Observability;

namespace Tubester.Observability;

/// <summary>
/// Implementation of Tubester metrics using System.Diagnostics.Metrics.
/// </summary>
public sealed class TubesterMetrics : ITubesterMetrics
{
    private readonly Counter<long> _commentScanStarted;
    private readonly Counter<long> _commentScanSucceeded;
    private readonly Counter<long> _commentScanFailed;

    private readonly Counter<long> _replyGenerationStarted;
    private readonly Counter<long> _replyGenerationSucceeded;
    private readonly Counter<long> _replyGenerationFailed;

    private readonly Counter<long> _embeddingGenerationStarted;
    private readonly Counter<long> _embeddingGenerationSucceeded;
    private readonly Counter<long> _embeddingGenerationFailed;

    private readonly Counter<long> _youTubeApiCalls;
    private readonly Counter<long> _youTubeApiErrors;

    private readonly Counter<long> _aiCalls;
    private readonly Counter<long> _aiCallErrors;

    public TubesterMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MetricNames.MeterName);

        _commentScanStarted = meter.CreateCounter<long>(
            MetricNames.CommentScanStarted,
            description: "Total number of comment scans started");

        _commentScanSucceeded = meter.CreateCounter<long>(
            MetricNames.CommentScanSucceeded,
            description: "Total number of comment scans that completed successfully");

        _commentScanFailed = meter.CreateCounter<long>(
            MetricNames.CommentScanFailed,
            description: "Total number of comment scans that failed");

        _replyGenerationStarted = meter.CreateCounter<long>(
            MetricNames.ReplyGenerationStarted,
            description: "Total number of reply generations started");

        _replyGenerationSucceeded = meter.CreateCounter<long>(
            MetricNames.ReplyGenerationSucceeded,
            description: "Total number of reply generations that completed successfully");

        _replyGenerationFailed = meter.CreateCounter<long>(
            MetricNames.ReplyGenerationFailed,
            description: "Total number of reply generations that failed");

        _embeddingGenerationStarted = meter.CreateCounter<long>(
            MetricNames.EmbeddingGenerationStarted,
            description: "Total number of embedding generations started");

        _embeddingGenerationSucceeded = meter.CreateCounter<long>(
            MetricNames.EmbeddingGenerationSucceeded,
            description: "Total number of embedding generations that completed successfully");

        _embeddingGenerationFailed = meter.CreateCounter<long>(
            MetricNames.EmbeddingGenerationFailed,
            description: "Total number of embedding generations that failed");

        _youTubeApiCalls = meter.CreateCounter<long>(
            MetricNames.YouTubeApiCalls,
            description: "Total number of YouTube API calls");

        _youTubeApiErrors = meter.CreateCounter<long>(
            MetricNames.YouTubeApiErrors,
            description: "Total number of YouTube API errors");

        _aiCalls = meter.CreateCounter<long>(
            MetricNames.AiCalls,
            description: "Total number of AI calls");

        _aiCallErrors = meter.CreateCounter<long>(
            MetricNames.AiCallErrors,
            description: "Total number of AI call errors");
    }

    public void CommentScanStarted() => _commentScanStarted.Add(1);

    public void CommentScanSucceeded() => _commentScanSucceeded.Add(1);

    public void CommentScanFailed() => _commentScanFailed.Add(1);

    public void ReplyGenerationStarted() => _replyGenerationStarted.Add(1);

    public void ReplyGenerationSucceeded() => _replyGenerationSucceeded.Add(1);

    public void ReplyGenerationFailed() => _replyGenerationFailed.Add(1);

    public void EmbeddingGenerationStarted() => _embeddingGenerationStarted.Add(1);

    public void EmbeddingGenerationSucceeded() => _embeddingGenerationSucceeded.Add(1);

    public void EmbeddingGenerationFailed() => _embeddingGenerationFailed.Add(1);

    public void YouTubeApiCall(string operation)
    {
        _youTubeApiCalls.Add(1, Tag("operation", NormalizeTagValue(operation)));
    }

    public void YouTubeApiError(string operation)
    {
        _youTubeApiErrors.Add(1, Tag("operation", NormalizeTagValue(operation)));
    }

    public void AiCall(string operation)
    {
        _aiCalls.Add(1, Tag("operation", NormalizeTagValue(operation)));
    }

    public void AiCallFailed(string operation)
    {
        _aiCallErrors.Add(1, Tag("operation", NormalizeTagValue(operation)));
    }

    private static KeyValuePair<string, object?> Tag(string key, object? value)
    {
        return new KeyValuePair<string, object?>(key, value);
    }

    private static string NormalizeTagValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? "unknown"
            : value.Trim().ToLowerInvariant();
    }
}