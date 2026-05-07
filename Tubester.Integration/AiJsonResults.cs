namespace Tubester.Integration;

internal sealed class AiMetadataJsonResult
{
    public string? Title { get; init; }

    public string? Description { get; init; }

    public List<string> Tags { get; init; } = [];
}

internal sealed class AiReplyJsonResult
{
    public string? Reply { get; init; }
}

internal sealed class AiPlaylistJsonResult
{
    public List<int> I { get; init; } = [];
}
