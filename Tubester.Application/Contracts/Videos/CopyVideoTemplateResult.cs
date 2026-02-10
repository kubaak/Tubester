namespace Tubester.Application.Contracts.Videos;

public sealed record CopyVideoTemplateResult(
    string SourceVideoId,
    string TargetVideoId,
    string FinalTitle,
    string FinalDescription,
    IReadOnlyList<string> AppliedTags,
    string? AppliedLocationDescription,
    (double lat, double lng)? AppliedLocationCoords,
    IReadOnlyList<string> PlaylistsAdded, // playlist IDs added
    bool CategoryCopied,
    bool DefaultLanguagesCopied
);