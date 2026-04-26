namespace Tubester.Domain;

public enum PlaylistVisibility
{
    Public = 0,
    Unlisted = 1,
    Private = 2
}

public sealed class Playlist : Entity
{
    public string PlaylistId { get; private set; } = default!;
    public string ChannelId { get; private set; } = default!;
    public string? Title { get; private set; }
    public string? Description { get; private set; }
    public string? ETag { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? LastMembershipSyncAt { get; private set; }
    public PlaylistVisibility Visibility { get; private set; } = PlaylistVisibility.Private;

    public static Playlist Create(string playlistId, string channelId, string? title, string? description, PlaylistVisibility visibility, DateTimeOffset updatedAt, string? etag = null)
    {
        return new Playlist
        {
            PlaylistId = playlistId,
            ChannelId = channelId,
            Title = title,
            Description = description,
            Visibility = visibility,
            ETag = etag,
            UpdatedAt = updatedAt,
            LastMembershipSyncAt = null
        };
    }

    public void UpdateTitle(string? title, string? description, PlaylistVisibility visibility, DateTimeOffset updatedAt, string? etag = null)
    {
        if (!StringComparer.Ordinal.Equals(Title, title) || !StringComparer.Ordinal.Equals(Description, description) || !StringComparer.Ordinal.Equals(ETag, etag) || Visibility != visibility)
        {
            Title = title;
            Description = description;
            Visibility = visibility;
            ETag = etag;
            UpdatedAt = updatedAt;
        }
    }

    public void SetLastMembershipSyncAt(DateTimeOffset syncedAt)
    {
        LastMembershipSyncAt = syncedAt;
        UpdatedAt = syncedAt;
    }

    private Playlist()
    {
    }
}