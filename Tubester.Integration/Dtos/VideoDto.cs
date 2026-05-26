namespace Tubester.Integration.Dtos;

public sealed record VideoDto(
    string VideoId,
    string Title,
    string Description,
    IEnumerable<string>? Tags,
    TimeSpan Duration,
    string PrivacyStatus,
    bool IsShort,
    DateTimeOffset PublishedAt,
    string? CategoryId,
    string? DefaultLanguage,
    string? DefaultAudioLanguage,
    string? ETag,
    bool? CommentsAllowed
);