namespace Tubester.Abstractions.ApplicationConfiguration;

/// <summary>
/// AI runtime options containing provider and model information.
/// </summary>
public sealed record AiRuntimeOptions(string Provider, string Model);