using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Tubester.Abstractions;
using Tubester.Abstractions.ApplicationConfiguration;
using Tubester.Application.Common;
using Tubester.Domain;
using Tubester.Persistence;

namespace Tubester.Application;

public sealed class ApplicationConfigurationService(
    TubesterDb dbContext,
    IMemoryCache cache,
    IDateTimeOffsetProvider dateTimeOffsetProvider)
    : IApplicationConfigurationService
{
    private static readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(1);

    public async Task<string?> GetValueAsync(string key, CancellationToken ct)
    {
        var normalizedKey = key.Trim();

        if (cache.TryGetValue<string?>($"config:{normalizedKey}", out var cachedValue))
        {
            return cachedValue;
        }

        var config = await dbContext.ApplicationConfigurations
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Key == normalizedKey, ct);

        var value = config?.Value;
        cache.Set($"config:{normalizedKey}", value, _cacheDuration);

        return value;
    }

    public async Task<T?> GetValueAsync<T>(string key, CancellationToken ct)
    {
        var value = await GetValueAsync(key, ct);
        if (value is null)
        {
            return default;
        }

        return ParseValue<T>(value);
    }

    public async Task<IReadOnlyList<ApplicationConfigurationDto>> GetAllAsync(CancellationToken ct)
    {
        var configs = await dbContext.ApplicationConfigurations
            .AsNoTracking()
            .OrderBy(c => c.Key)
            .ToListAsync(ct);

        return configs.Select(MapToDto).ToList();
    }

    public async Task<ApplicationConfigurationDto?> GetByKeyAsync(string key, CancellationToken ct)
    {
        var normalizedKey = key.Trim();

        var config = await dbContext.ApplicationConfigurations
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Key == normalizedKey, ct);

        return config is null ? null : MapToDto(config);
    }

    public async Task<ApplicationConfigurationDto> CreateAsync(CreateApplicationConfigurationRequest request, CancellationToken ct)
    {
        var key = request.Key.Trim();

        if (string.IsNullOrWhiteSpace(key))
        {
            throw new BadRequestException("Key must not be empty.");
        }

        var existing = await dbContext.ApplicationConfigurations
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Key == key, ct);

        if (existing is not null)
        {
            throw new ConflictException($"Configuration with key '{key}' already exists.");
        }

        ValidateValue(request.Value, request.ValueType);

        var nowUtc = dateTimeOffsetProvider.GetUtcNowDateTimeOffset();
        var config = ApplicationConfiguration.Create(
            key: key,
            value: request.Value,
            valueType: request.ValueType,
            description: request.Description,
            isSystem: request.IsSystem,
            updatedAtUtc: nowUtc);

        dbContext.ApplicationConfigurations.Add(config);
        await dbContext.SaveChangesAsync(ct);

        return MapToDto(config);
    }

    public async Task<ApplicationConfigurationDto?> UpdateAsync(string key, UpdateApplicationConfigurationRequest request, CancellationToken ct)
    {
        var normalizedKey = key.Trim();

        var config = await dbContext.ApplicationConfigurations
            .FirstOrDefaultAsync(c => c.Key == normalizedKey, ct);

        if (config is null)
        {
            return null;
        }

        ValidateValue(request.Value, request.ValueType);

        var nowUtc = dateTimeOffsetProvider.GetUtcNowDateTimeOffset();
        config.Update(
            value: request.Value,
            valueType: request.ValueType,
            description: request.Description,
            updatedAtUtc: nowUtc);

        await dbContext.SaveChangesAsync(ct);

        cache.Remove($"config:{normalizedKey}");

        return MapToDto(config);
    }

    public async Task<bool> DeleteAsync(string key, CancellationToken ct)
    {
        var normalizedKey = key.Trim();

        var config = await dbContext.ApplicationConfigurations
            .FirstOrDefaultAsync(c => c.Key == normalizedKey, ct);

        if (config is null)
        {
            return false;
        }

        if (config.IsSystem)
        {
            throw new BadRequestException("Cannot delete system configuration.");
        }

        dbContext.ApplicationConfigurations.Remove(config);
        await dbContext.SaveChangesAsync(ct);

        cache.Remove($"config:{normalizedKey}");

        return true;
    }

    private static void ValidateValue(string value, ConfigurationValueType valueType)
    {
        switch (valueType)
        {
            case ConfigurationValueType.Boolean:
                if (!bool.TryParse(value, out _))
                {
                    throw new BadRequestException($"Value '{value}' is not a valid boolean.");
                }
                break;

            case ConfigurationValueType.Integer:
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                {
                    throw new BadRequestException($"Value '{value}' is not a valid integer.");
                }
                break;

            case ConfigurationValueType.Decimal:
                if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
                {
                    throw new BadRequestException($"Value '{value}' is not a valid decimal.");
                }
                break;

            case ConfigurationValueType.Json:
                try
                {
                    using var doc = JsonDocument.Parse(value);
                }
                catch (JsonException ex)
                {
                    throw new BadRequestException($"Value is not valid JSON: {ex.Message}");
                }
                break;

            case ConfigurationValueType.String:
                if (value is null)
                {
                    throw new BadRequestException(nameof(value));
                }
                break;
        }
    }

    private static T? ParseValue<T>(string value)
    {
        var targetType = typeof(T);

        if (targetType == typeof(string))
        {
            return (T)(object)value;
        }

        if (targetType == typeof(bool))
        {
            if (bool.TryParse(value, out var result))
            {
                return (T)(object)result;
            }
            return default;
        }

        if (targetType == typeof(int))
        {
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
            {
                return (T)(object)result;
            }
            return default;
        }

        if (targetType == typeof(decimal))
        {
            if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var result))
            {
                return (T)(object)result;
            }
            return default;
        }

        return JsonSerializer.Deserialize<T>(value);
    }

    private static ApplicationConfigurationDto MapToDto(ApplicationConfiguration config)
    {
        return new ApplicationConfigurationDto(
            config.Key,
            config.Value,
            config.ValueType,
            config.Description,
            config.IsSystem,
            config.UpdatedAtUtc);
    }
}
