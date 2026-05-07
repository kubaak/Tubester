using System.Text.Json;
using Microsoft.Extensions.Logging;
using Tubester.Abstractions;

namespace Tubester.Integration;

internal static class AiJsonResponseParser
{
    public static T DeserializeModelJson<T>(
        string responseText,
        ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(responseText))
        {
            throw new InvalidOperationException("AI returned an empty response.");
        }

        var json = ExtractJson(responseText);

        try
        {
            var result = JsonSerializer.Deserialize<T>(
                json,
                TubesterJsonSerializerOptions.DefaultRead);

            if (result is null)
            {
                logger.LogDebug("AI JSON response: {Response}", json);
                throw new InvalidOperationException(
                    $"Failed to deserialize AI response to expected type {typeof(T).Name}.");
            }

            return result;
        }
        catch (JsonException ex)
        {
            logger.LogDebug("AI JSON response: {Response}", json);
            throw new InvalidOperationException("Failed to parse AI JSON response.", ex);
        }
    }

    private static string ExtractJson(string text)
    {
        var trimmed = text.Trim();

        if (IsValidJson(trimmed))
        {
            return trimmed;
        }

        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            trimmed = trimmed
                .Replace("```json", "", StringComparison.OrdinalIgnoreCase)
                .Replace("```", "", StringComparison.Ordinal)
                .Trim();

            if (IsValidJson(trimmed))
            {
                return trimmed;
            }
        }

        var jsonStart = trimmed.IndexOf('{');
        var jsonEnd = trimmed.LastIndexOf('}');

        if (jsonStart >= 0 && jsonEnd > jsonStart)
        {
            var extracted = trimmed[jsonStart..(jsonEnd + 1)];

            if (IsValidJson(extracted))
            {
                return extracted;
            }
        }

        var repaired = TryRepairJsonObject(trimmed);

        if (repaired is not null)
        {
            return repaired;
        }

        return trimmed;
    }

    private static string? TryRepairJsonObject(string value)
    {
        var candidate = value.Trim();

        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        if (!candidate.StartsWith('{'))
        {
            candidate = "{" + candidate;
        }

        if (!candidate.EndsWith('}'))
        {
            candidate += "}";
        }

        return IsValidJson(candidate)
            ? candidate
            : null;
    }

    private static bool IsValidJson(string value)
    {
        try
        {
            using var _ = JsonDocument.Parse(value);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}