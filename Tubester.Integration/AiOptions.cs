namespace Tubester.Integration;

public sealed class AiOptions
{
    public string Provider { get; set; } = "ollama";
    public string Endpoint { get; set; } = "http://localhost:11434";
    public string Model { get; set; } = "gemma3:12b";
}