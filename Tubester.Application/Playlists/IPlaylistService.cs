using Tubester.Application.Contracts.Videos;

namespace Tubester.Application.Playlists;

public interface IPlaylistService
{
    Task<IReadOnlyList<PlaylistDto>> ListAsync(string channelId, CancellationToken cancellationToken);
}
