namespace Tubester.Integration.Dtos;

public sealed record PlaylistDto(
    string Id,
    string? Title,
    string? Description,
    string Visibility,
    string? ETag
);
