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
    /// Indicates that an AI title generation is currently in progress for this video.
    /// </summary>
    public required bool IsAiTitleInProgress { get; init; }

    /// <summary>
    /// Indicates that an AI description generation is currently in progress for this video.
    /// </summary>
    public required bool IsAiDescriptionInProgress { get; init; }

    /// <summary>
    /// Indicates that an AI tags generation is currently in progress for this video.
    /// </summary>
    public required bool IsAiTagsInProgress { get; init; }
    /// <summary>
    /// Indicates that an AI playlist suggestion is currently in progress for this video.
    /// </summary>
    public required bool IsAiPlaylistSuggestionInProgress { get; init; }

    /// <summary>
    /// Playlists this video belongs to.
    /// </summary>
    public required PlaylistDto[] Playlists { get; init; }

    /// <summary>
    /// YouTube video category.
    /// </summary>
    public CategoryDto? Category { get; init; }

    /// <summary>
    /// Default language of the video content.
    /// </summary>
    public string? DefaultLanguage { get; init; }

    /// <summary>
    /// Default audio language of the video content.
    /// </summary>
    public string? DefaultAudioLanguage { get; init; }
}
