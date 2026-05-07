namespace Tubester.Abstractions.ApplicationConfiguration;

/// <summary>
/// Constants for application configuration keys.
/// </summary>
public static class ApplicationConfigurationKeys
{
    /// <summary>
    /// The AI provider configuration key.
    /// </summary>
    public const string AiProvider = "Ai:Provider";

    /// <summary>
    /// The AI model configuration key.
    /// </summary>
    public const string AiModel = "Ai:Model";

    /// <summary>
    /// Model used specifically for playlist suggestion.
    /// </summary>
    public const string AiPlaylistModel = "Ai:PlaylistModel";

    /// <summary>
    /// The temperature configuration key.
    /// </summary>
    public const string AiTemperature = "Ai:Temperature";

    /// <summary>
    /// Temperature for playlist suggestion.
    /// </summary>
    public const string AiPlaylistTemperature = "Ai:PlaylistTemperature";

    /// <summary>
    /// Context window size for playlist suggestion.
    /// </summary>
    public const string AiPlaylistNumCtx = "Ai:PlaylistNumCtx";

    /// <summary>
    /// Maximum number of tokens to generate for AI metadata.
    /// </summary>
    public const string AiDetailsMaxOutputTokens = "Ai:DetailsMaxOutputTokens";

    /// <summary>
    /// Maximum number of tokens to generate for AI replies.
    /// </summary>
    public const string AiReplyMaxOutputTokens = "Ai:ReplyMaxOutputTokens";

    /// <summary>
    /// Maximum number of tokens to generate for playlist suggestions.
    /// </summary>
    public const string AiPlaylistMaxOutputTokens = "Ai:PlaylistMaxOutputTokens";
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
    /// Gemini provider.
    /// </summary>
    public const string Gemini = "Gemini";

    /// <summary>
    /// All registered providers.
    /// </summary>
    public static readonly string[] All = { Ollama, Gemini };
}
