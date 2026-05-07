using Tubester.Abstractions.ApplicationConfiguration;

namespace Tubester.Integration;

/// <summary>
/// Interface for provider-specific text generation clients.
/// </summary>
public interface IAiTextGenerationClient
{
    /// <summary>
    /// The provider name (e.g., "Ollama", "Gemini").
    /// </summary>
    string Provider { get; }

    /// <summary>
    /// Generates text using the AI provider.
    /// </summary>
    /// <param name="operation">The type of operation being performed.</param>
    /// <param name="prompt">The prompt to send to the AI.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The text response and usage metadata from the AI provider.</returns>
    Task<AiTextGenerationResult> GenerateTextAsync(
        AiOperation operation,
        string prompt,
        CancellationToken cancellationToken);
}