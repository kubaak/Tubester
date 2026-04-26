namespace Tubester.Integration;

public sealed class AiOptions
{
    public string Provider { get; set; } = "ollama";
    public string Endpoint { get; set; } = "http://localhost:11434";
    public string Model { get; set; } = "gemma3:12b";
    public int MaxPlaylistsPerBatch { get; set; } = 50;

    /// <summary>
    /// Model used specifically for playlist suggestion.
    /// </summary>
    public string PlaylistModel { get; set; } = "";

    /// <summary>
    /// Temperature for playlist suggestion.
    /// </summary>
    public double PlaylistTemperature { get; set; } = 1;

    /// <summary>
    /// Context window size for playlist suggestion.
    /// </summary>
    public int PlaylistNumCtx { get; set; } = 4096;
}
