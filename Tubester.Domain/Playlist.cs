namespace Tubester.Domain;

public sealed class Playlist
{
    public string PlaylistId { get; private set; } = default!;
    public string ChannelId { get; private set; } = default!;
    public string? Title { get; private set; }
    public string? ETag { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? LastMembershipSyncAt { get; private set; }

    public static Playlist Create(string playlistId, string channelId, string? title, DateTimeOffset updatedAt, string? etag = null)
    {
        return new Playlist
        {
            PlaylistId = playlistId,
            ChannelId = channelId,
            Title = title,
            ETag = etag,
            UpdatedAt = updatedAt,
            LastMembershipSyncAt = null
        };
    }

    public void UpdateTitle(string? title, DateTimeOffset updatedAt, string? etag = null)
    {
        if (!StringComparer.Ordinal.Equals(Title, title) || !StringComparer.Ordinal.Equals(ETag, etag))
        {
            Title = title;
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