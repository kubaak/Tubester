namespace Tubester.Integration;

/// <summary>
/// Result of AI text generation including the generated text and usage metadata.
/// </summary>
public sealed record AiTextGenerationResult(
    string Text,
    AiUsage Usage);

/// <summary>
/// AI usage metadata for telemetry purposes.
/// </summary>
public sealed record AiUsage(
    string Provider,
    string Model,
    int? PromptTokens,
    int? CompletionTokens,
    int? TotalTokens,
    int? MaxOutputTokens,
    double? Temperature,
    TimeSpan Duration);