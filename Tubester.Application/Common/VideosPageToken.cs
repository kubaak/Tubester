using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace Tubester.Application.Common;

public static class VideosPageToken
{
    public static string Serialize(DateTimeOffset publishedAtUtc, string videoId, string? binding)
    {
        ArgumentException.ThrowIfNullOrEmpty(videoId);

        // payload: ISO8601 | videoId | optional binding
        var core = $"{publishedAtUtc:O}|{videoId}";
        var payload = string.IsNullOrEmpty(binding) ? core : $"{core}|{binding}";
        var bytes = Encoding.UTF8.GetBytes(payload);

        return WebEncoders.Base64UrlEncode(bytes);
    }

    public static bool TryParse(string? token, out DateTimeOffset publishedAtUtc, out string videoId,
        out string? binding)
    {
        publishedAtUtc = default;
        videoId = string.Empty;
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
                    out publishedAtUtc))
            {
                return false;
            }

            videoId = parts[1];
            if (string.IsNullOrEmpty(videoId))
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