using Tubester.Domain;

namespace Tubester.Abstractions.ApplicationConfiguration;

/// <summary>
/// Request to update an application configuration.
/// </summary>
public sealed record UpdateApplicationConfigurationRequest(
    string Value,
    ConfigurationValueType ValueType,
    string? Description);