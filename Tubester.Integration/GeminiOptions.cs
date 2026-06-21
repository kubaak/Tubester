namespace Tubester.Integration;

public class GeminiOptions
{
    public string ApiKey { get; init; } = null!;
    public string EmbeddingModel { get; set; } = "gemini-embedding-001";
    public int OutputDimensionality { get; set; } = 768;
}