using System.Text.Json;
using Microsoft.Extensions.Logging;
using Tubester.Abstractions;

namespace Tubester.Integration;

internal sealed class AiJsonResponseParser(ILogger<AiJsonResponseParser> logger) : IAiJsonResponseParser
{
    public T DeserializeModelResponse<T>(string responseText)
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
            logger.LogDebug(
                ex,
                "AI JSON response could not be deserialized to {Type}. Extracted response: {Response}",
                typeof(T).Name,
                json);

            throw new InvalidOperationException(
                $"Failed to deserialize AI JSON response to expected type {typeof(T).Name}.",
                ex);
        }
    }

    private static string ExtractJson(string text)
    {
        var trimmed = text.Trim();

        if (IsValidJson(trimmed))
        {
            return trimmed;
        }

        var fenced = TryExtractMarkdownFence(trimmed);
        if (fenced is not null)
        {
            if (IsValidJson(fenced))
            {
                return fenced;
            }

            var repairedFence = TryRepairTruncatedJson(fenced);
            if (repairedFence is not null)
            {
                return repairedFence;
            }
        }

        var extractedObject = TryExtractJsonBetween(trimmed, '{', '}');
        if (extractedObject is not null)
        {
            if (IsValidJson(extractedObject))
            {
                return extractedObject;
            }

            var repairedObject = TryRepairTruncatedJson(extractedObject);
            if (repairedObject is not null)
            {
                return repairedObject;
            }
        }

        var extractedArray = TryExtractJsonBetween(trimmed, '[', ']');
        if (extractedArray is not null)
        {
            if (IsValidJson(extractedArray))
            {
                return extractedArray;
            }

            var repairedArray = TryRepairTruncatedJson(extractedArray);
            if (repairedArray is not null)
            {
                return repairedArray;
            }
        }

        var repairedCandidate = TryRepairTruncatedJson(trimmed);
        if (repairedCandidate is not null)
        {
            return repairedCandidate;
        }

        return trimmed;
    }

    private static string? TryExtractMarkdownFence(string value)
    {
        var trimmed = value.Trim();

        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return null;
        }

        var firstLineEnd = trimmed.IndexOf('\n');
        if (firstLineEnd < 0)
        {
            return null;
        }

        var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        if (lastFence <= firstLineEnd)
        {
            return null;
        }

        return trimmed[(firstLineEnd + 1)..lastFence].Trim();
    }

    private static string? TryExtractJsonBetween(string value, char open, char close)
    {
        var start = value.IndexOf(open);
        if (start < 0)
        {
            return null;
        }

        var end = value.LastIndexOf(close);

        if (end <= start)
        {
            return value[start..];
        }

        var extracted = value[start..(end + 1)];

        return IsValidJson(extracted)
            ? extracted
            : null;
    }

    private static string? TryRepairTruncatedJson(string value)
    {
        var candidate = value.Trim();

        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        var jsonStart = FindFirstJsonContainerStart(candidate);

        if (jsonStart >= 0)
        {
            candidate = candidate[jsonStart..];
        }
        else if (LooksLikeJsonObjectProperties(candidate))
        {
            candidate = "{" + candidate;
        }
        else
        {
            return null;
        }

        if (!candidate.StartsWith('{') && !candidate.StartsWith('['))
        {
            return null;
        }

        var repaired = CloseOpenJsonContainers(candidate);

        return repaired is not null && IsValidJson(repaired)
            ? repaired
            : null;
    }

    private static bool LooksLikeJsonObjectProperties(string value)
    {
        var trimmed = value.TrimStart();

        if (!trimmed.StartsWith('"'))
        {
            return false;
        }

        var inString = false;
        var escaping = false;

        foreach (var character in trimmed)
        {
            if (escaping)
            {
                escaping = false;
                continue;
            }

            if (character == '\\' && inString)
            {
                escaping = true;
                continue;
            }

            if (character == '"')
            {
                inString = !inString;
                continue;
            }

            if (!inString && character == ':')
            {
                return true;
            }
        }

        return false;
    }

    private static int FindFirstJsonContainerStart(string value)
    {
        var objectStart = value.IndexOf('{');
        var arrayStart = value.IndexOf('[');

        return objectStart switch
        {
            >= 0 when arrayStart >= 0 => Math.Min(objectStart, arrayStart),
            >= 0 => objectStart,
            _ when arrayStart >= 0 => arrayStart,
            _ => -1
        };
    }

    private static string? CloseOpenJsonContainers(string value)
    {
        var stack = new Stack<char>();
        var inString = false;
        var escaping = false;

        foreach (var character in value)
        {
            if (escaping)
            {
                escaping = false;
                continue;
            }

            if (character == '\\' && inString)
            {
                escaping = true;
                continue;
            }

            if (character == '"')
            {
                inString = !inString;
                continue;
            }

            if (inString)
            {
                continue;
            }

            switch (character)
            {
                case '{':
                    stack.Push('}');
                    break;

                case '[':
                    stack.Push(']');
                    break;

                case '}':
                case ']':
                    if (stack.Count == 0)
                    {
                        return null;
                    }

                    if (stack.Peek() != character)
                    {
                        return null;
                    }

                    stack.Pop();
                    break;
            }
        }

        // Do not repair truncated strings.
        // This prevents accepting incomplete values like:
        // {
        //   "title": "Title",
        //   "description": "This was cut off
        if (inString || escaping)
        {
            return null;
        }

        while (stack.Count > 0)
        {
            value += stack.Pop();
        }

        return value;
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