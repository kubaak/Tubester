using Tubester.Domain;

namespace Tubester.Application.Common;

public static class VideoVisibilityMapper
{
    public static VideoVisibility MapVisibility(string? privacyStatus, DateTimeOffset? publishAtUtc, DateTimeOffset nowUtc)
    {
        if (string.Equals(privacyStatus, "private", StringComparison.OrdinalIgnoreCase)
            && publishAtUtc.HasValue
            && publishAtUtc.Value > nowUtc)
        {
            return VideoVisibility.Scheduled;
        }

        if (!string.IsNullOrWhiteSpace(privacyStatus) &&
            Enum.TryParse<VideoVisibility>(privacyStatus, true, out var parsed))
        {
            return parsed;
        }

        return VideoVisibility.Private;
    }
}