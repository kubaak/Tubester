namespace Tubester.Abstractions.ApplicationConfiguration;

/// <summary>
/// Constants for application configuration keys.
/// </summary>
public static class ApplicationConfigurationKeys
{
    /// <summary>
    /// The AI provider configuration key.
    /// </summary>
    public const string AiProvider = "Ai.Provider";

    /// <summary>
    /// The AI model configuration key.
    /// </summary>
    public const string AiModel = "Ai.Model";
}

/// <summary>
/// Constants for supported AI providers.
/// </summary>
public static class AiProviders
{
    /// <summary>
    /// Ollama provider.
    /// </summary>
    public const string Ollama = "Ollama";

    /// <summary>
    /// OpenAI provider.
    /// </summary>
    public const string OpenAi = "OpenAi";

    /// <summary>
    /// All registered providers.
    /// </summary>
    public static readonly string[] All = { Ollama, OpenAi };
}
