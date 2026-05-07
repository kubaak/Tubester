namespace Tubester.Application.Contracts.Videos;

public sealed record SaveVideoDraftRequest(
    string VideoId,
    string? Title,
    string? Description,
    IReadOnlyList<string>? Tags,
    IReadOnlyList<string>? PlaylistIds
);
