using Tubester.Domain;

namespace Tubester.Abstractions.ApplicationConfiguration;

/// <summary>
/// Request to create a new application configuration.
/// </summary>
public sealed record CreateApplicationConfigurationRequest(
    string Key,
    string Value,
    ConfigurationValueType ValueType,
    string? Description,
    bool IsSystem);