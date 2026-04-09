namespace Tubester.Application.Contracts.Videos;

/// <summary>
/// Represents a playlist a video belongs to.
/// </summary>
public sealed record PlaylistDto(string Id, string? Name);
