namespace Tubester.Application.Contracts.Videos;

public sealed record CopyVideoTemplateRequest(
    string SourceVideoId,
    string TargetVideoId,
    bool CopyTags = true,
    bool CopyPlaylists = true,
    bool CopyCategory = true,
    bool CopyDefaultLanguages = true
);