namespace Tubester.Integration;

public sealed class SuggestedMetadata
{
    public string? Title { get; init; }
    public string? Description { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
}