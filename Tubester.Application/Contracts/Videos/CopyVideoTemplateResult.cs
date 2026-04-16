namespace Tubester.Application.Contracts.Videos;

public sealed record CopyVideoTemplateResult(
    string SourceVideoId,
    string TargetVideoId,
    string FinalTitle,
    string FinalDescription,
    IReadOnlyList<string> AppliedTags,
    IReadOnlyList<string> PlaylistsAdded, // playlist IDs added
    bool TitleCopied,
    bool DescriptionCopied,
    bool CategoryCopied,
    bool DefaultLanguagesCopied
);
