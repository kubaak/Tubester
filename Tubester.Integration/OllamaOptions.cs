namespace Tubester.Integration;

public class OllamaOptions
{
    public string Endpoint { get; set; } = "http://localhost:11434";
    public int MetadataNumCtx { get; init; } = 4096;
    public int MetadataNumPredict { get; init; } = 256;
    public int ReplyNumCtx { get; init; } = 4096;
    public int ReplyNumPredict { get; init; } = 256;
    public int PlaylistNumCtx { get; init; } = 4096;
    public int PlaylistNumPredict { get; init; } = 256;
}