using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace Tubester.Application.Common;

/// <summary>
/// Cursor-based pagination token for paginated results.
/// Can be reused for any entity type that uses DateTimeOffset + string cursor ordering.
/// </summary>
public static class PageToken
{
    /// <summary>
    /// Serializes cursor-based pagination data into a Base64URL-encoded token.
    /// </summary>
    /// <param name="timestampUtc">The timestamp used for ordering (e.g., PublishedAt, PulledAt).</param>
    /// <param name="cursor">The cursor identifier to use after the timestamp for tie-breaking.</param>
    /// <param name="binding">Optional binding string for filter validation.</param>
    /// <returns>A Base64URL-encoded token string.</returns>
    public static string Serialize(DateTimeOffset timestampUtc, string cursor, string? binding = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(cursor);

        // payload: ISO8601 | cursor | optional binding
        var core = $"{timestampUtc:O}|{cursor}";
        var payload = string.IsNullOrEmpty(binding) ? core : $"{core}|{binding}";
        var bytes = Encoding.UTF8.GetBytes(payload);

        return WebEncoders.Base64UrlEncode(bytes);
    }

    /// <summary>
    /// Attempts to parse a Base64URL-encoded token.
    /// </summary>
    /// <param name="token">The token string to parse.</param>
    /// <param name="timestampUtc">When successful, contains the parsed timestamp.</param>
    /// <param name="cursor">When successful, contains the parsed cursor identifier.</param>
    /// <param name="binding">When successful, contains the optional binding string if present.</param>
    /// <returns>True if parsing succeeded, false otherwise.</returns>
    public static bool TryParse(string? token, out DateTimeOffset timestampUtc, out string cursor, out string? binding)
    {
        timestampUtc = default;
        cursor = string.Empty;
        binding = null;

        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        try
        {
            // Decode Base64URL
            var bytes = WebEncoders.Base64UrlDecode(token);
            var payload = Encoding.UTF8.GetString(bytes);

            // limit to 3 parts so binding may contain '|'
            var parts = payload.Split('|', 3);
            if (parts.Length < 2)
            {
                return false;
            }

            if (!DateTimeOffset.TryParseExact(parts[0], "O",
                    CultureInfo.InvariantCulture, DateTimeStyles.None,
                    out timestampUtc))
            {
                return false;
            }

            cursor = parts[1];
            if (string.IsNullOrEmpty(cursor))
            {
                return false;
            }

            if (parts.Length == 3)
            {
                binding = parts[2];
            }

            return true;
        }
        catch
        {
            return false;
        }
    }
}
