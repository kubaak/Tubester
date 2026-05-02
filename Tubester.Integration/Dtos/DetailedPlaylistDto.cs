namespace Tubester.Integration.Dtos;

public sealed record DetailedPlaylistDto(
    string Id,
    string? Title,
    string? Description,
    string Visibility,
    string? ETag
);
