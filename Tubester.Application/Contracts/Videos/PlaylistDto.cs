namespace Tubester.Application.Contracts.Videos;

/// <summary>
/// Represents a playlist a video belongs to.
/// </summary>
public sealed record PlaylistDto
{
    public required string Id { get; init; }
    public string? Name { get; init; }
}
