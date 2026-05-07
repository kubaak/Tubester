using Tubester.Abstractions.Playlists;

namespace Tubester.Integration;

internal static class AiSuggestionNormalizer
{
    private const int MaxTitleLength = 100;
    private const int MaxDescriptionLength = 5000;
    private const int MaxTags = 15;

    public static SuggestedMetadata ToSuggestedMetadata(
        AiMetadataJsonResult result,
        bool generateTitle,
        bool generateDescription,
        bool generateTags)
    {
        return new SuggestedMetadata
        {
            Title = generateTitle ? NormalizeTitle(result.Title) : null,
            Description = generateDescription ? NormalizeDescription(result.Description) : null,
            Tags = generateTags ? NormalizeTags(result.Tags) : []
        };
    }

    public static string? NormalizeReply(string? reply)
    {
        var normalized = reply?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    public static List<string> NormalizeTags(IEnumerable<string?> tags)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var tag in tags)
        {
            var normalized = NormalizeTag(tag);

            if (string.IsNullOrWhiteSpace(normalized))
            {
                continue;
            }

            if (!seen.Add(normalized))
            {
                continue;
            }

            result.Add(normalized);

            if (result.Count >= MaxTags)
            {
                break;
            }
        }

        return result;
    }

    public static IReadOnlyList<string> MapPlaylistIndexesToIds(
        IEnumerable<int> indexes,
        IReadOnlyList<PlaylistCandidateDto> candidates)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var index in indexes)
        {
            if (index < 0)
            {
                continue;
            }

            if (index >= candidates.Count)
            {
                continue;
            }

            var playlistId = candidates[index].PlaylistId;

            if (string.IsNullOrWhiteSpace(playlistId))
            {
                continue;
            }

            if (!seen.Add(playlistId))
            {
                continue;
            }

            result.Add(playlistId);
        }

        return result;
    }

    private static string? NormalizeTitle(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();

        if (normalized.Length > MaxTitleLength)
        {
            normalized = normalized[..MaxTitleLength].Trim();
        }

        return normalized;
    }

    private static string? NormalizeDescription(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();

        if (normalized.Length > MaxDescriptionLength)
        {
            normalized = normalized[..MaxDescriptionLength].Trim();
        }

        return normalized;
    }

    private static string? NormalizeTag(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value
            .Trim()
            .TrimStart('#')
            .Trim();
    }
}