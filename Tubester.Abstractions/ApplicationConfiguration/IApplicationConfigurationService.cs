namespace Tubester.Abstractions.ApplicationConfiguration;

/// <summary>
/// Service for managing application configurations.
/// </summary>
public interface IApplicationConfigurationService
{
    /// <summary>
    /// Gets a configuration value by key as a string.
    /// </summary>
    Task<string?> GetValueAsync(string key, CancellationToken ct);

    /// <summary>
    /// Gets a configuration value by key, deserialized to the specified type.
    /// </summary>
    Task<T?> GetValueAsync<T>(string key, CancellationToken ct);

    /// <summary>
    /// Gets all application configurations.
    /// </summary>
    Task<IReadOnlyList<ApplicationConfigurationDto>> GetAllAsync(CancellationToken ct);

    /// <summary>
    /// Gets an application configuration by key.
    /// </summary>
    Task<ApplicationConfigurationDto?> GetByKeyAsync(string key, CancellationToken ct);

    /// <summary>
    /// Creates a new application configuration.
    /// </summary>
    Task<ApplicationConfigurationDto> CreateAsync(CreateApplicationConfigurationRequest request, CancellationToken ct);

    /// <summary>
    /// Updates an existing application configuration.
    /// </summary>
    Task<ApplicationConfigurationDto?> UpdateAsync(string key, UpdateApplicationConfigurationRequest request, CancellationToken ct);

    /// <summary>
    /// Deletes an application configuration.
    /// </summary>
    Task<bool> DeleteAsync(string key, CancellationToken ct);
}