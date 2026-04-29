namespace Tubester.Domain;

/// <summary>
/// Represents a runtime application configuration entry.
/// </summary>
public sealed class ApplicationConfiguration
{
    /// <summary>
    /// Configuration key (e.g., "Ai.Provider", "Feature.PlaylistSuggest").
    /// </summary>
    public string Key { get; private set; } = null!;

    /// <summary>
    /// The configuration value.
    /// </summary>
    public string Value { get; private set; } = null!;

    /// <summary>
    /// The type of the configuration value.
    /// </summary>
    public ConfigurationValueType ValueType { get; private set; }

    /// <summary>
    /// Optional description of what this configuration controls.
    /// </summary>
    public string? Description { get; private set; }

    /// <summary>
    /// Indicates whether this is a system configuration that cannot be deleted.
    /// </summary>
    public bool IsSystem { get; private set; }

    /// <summary>
    /// When this configuration was last updated.
    /// </summary>
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    private ApplicationConfiguration()
    {
    }

    public static ApplicationConfiguration Create(
        string key,
        string value,
        ConfigurationValueType valueType,
        string? description,
        bool isSystem,
        DateTimeOffset updatedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Key must not be empty.", nameof(key));
        }

        return new ApplicationConfiguration
        {
            Key = key.Trim(),
            Value = value ?? throw new ArgumentNullException(nameof(value)),
            ValueType = valueType,
            Description = description?.Trim(),
            IsSystem = isSystem,
            UpdatedAtUtc = updatedAtUtc
        };
    }

    public void Update(
        string value,
        ConfigurationValueType valueType,
        string? description,
        DateTimeOffset updatedAtUtc)
    {
        Value = value ?? throw new ArgumentNullException(nameof(value));
        ValueType = valueType;
        Description = description?.Trim();
        UpdatedAtUtc = updatedAtUtc;
    }
}

/// <summary>
/// The type of a configuration value.
/// </summary>
public enum ConfigurationValueType
{
    String,
    Boolean,
    Integer,
    Decimal,
    Json
}
