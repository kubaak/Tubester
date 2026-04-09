using Tubester.Domain;

namespace Tubester.Abstractions.Playlists;

public interface IPlaylistRepository
{
    Task<List<Playlist>> GetByChannelAsync(string channelId, CancellationToken cancellationToken);

    Task<List<string>> GetPlaylistIdsByVideoAsync(string videoId, CancellationToken cancellationToken);

    Task<List<Playlist>> GetPlaylistsByVideoAsync(string videoId, CancellationToken cancellationToken);

    Task<Playlist?> GetAsync(string playlistId, CancellationToken cancellationToken);

    Task<(int inserted, int updated)> UpsertAsync(
        IEnumerable<Playlist> playlists,
        CancellationToken cancellationToken);

    Task<HashSet<string>> GetMembershipVideoIdsAsync(string playlistId, CancellationToken cancellationToken);

    Task<int> SetMembershipsToPlaylistsAsync(
        string videoId,
        HashSet<string> playlistIds,
        CancellationToken cancellationToken);

    Task<int> AddMembershipsAsync(
        string playlistId,
        HashSet<string> videoIds,
        CancellationToken cancellationToken);

    Task<int> RemoveMembershipsAsync(
        string playlistId,
        IEnumerable<string> videoIds,
        CancellationToken cancellationToken);

    Task UpdateLastMembershipSyncAtAsync(
        string playlistId,
        DateTimeOffset syncedAt,
        CancellationToken cancellationToken);

    Task<Dictionary<string, string?>> GetPlaylistETagsAsync(
        IEnumerable<string> playlistIds,
        CancellationToken cancellationToken);
}