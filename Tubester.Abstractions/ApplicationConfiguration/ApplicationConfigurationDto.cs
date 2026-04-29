using Tubester.Domain;

namespace Tubester.Abstractions.ApplicationConfiguration;

/// <summary>
/// DTO for application configuration.
/// </summary>
public sealed record ApplicationConfigurationDto(
    string Key,
    string Value,
    ConfigurationValueType ValueType,
    string? Description,
    bool IsSystem,
    DateTimeOffset UpdatedAtUtc);