namespace Tubester.Abstractions;

public sealed record RelevantReplyExample(
    string CommentText,
    string ReplyText,
    string? VideoId,
    string? VideoTitle,
    double SimilarityScore);