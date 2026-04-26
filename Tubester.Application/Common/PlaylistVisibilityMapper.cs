using Microsoft.Extensions.Logging;
using Tubester.Domain;

namespace Tubester.Application.Common;

public static class PlaylistVisibilityMapper
{
    public static PlaylistVisibility MapVisibility(string? privacyStatus, ILogger? logger = null)
    {
        if (!string.IsNullOrWhiteSpace(privacyStatus) &&
            Enum.TryParse<PlaylistVisibility>(privacyStatus, true, out var parsed))
        {
            return parsed;
        }

        logger?.LogWarning("Failed to parse playlist visibility '{PrivacyStatus}', defaulting to Private", privacyStatus);
        return PlaylistVisibility.Private;
    }
}
