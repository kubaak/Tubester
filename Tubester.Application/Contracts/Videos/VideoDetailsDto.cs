namespace Tubester.Application.Contracts.Videos;

/// <summary>
/// Represents editable metadata for a single video.
/// </summary>
public sealed record VideoDetailsDto
{
    /// <summary>
    /// Video title.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// Video description.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Video tags.
    /// </summary>
    public required string[] Tags { get; init; }

    /// <summary>
    /// Indicates that an AI template job is currently in progress for this video.
    /// </summary>
    public bool IsAiTemplateInProgress { get; init; }
}
