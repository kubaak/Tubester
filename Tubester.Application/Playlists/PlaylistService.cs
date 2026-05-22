using Tubester.Abstractions.Playlists;
using Tubester.Application.Contracts.Videos;

namespace Tubester.Application.Playlists;

public sealed class PlaylistService(IPlaylistRepository playlistRepository) : IPlaylistService
{
    public async Task<IReadOnlyList<PlaylistDto>> ListAsync(
        string channelId,
        CancellationToken cancellationToken)
    {
        var playlists = await playlistRepository.GetByChannelAsync(channelId, cancellationToken);

        return playlists
            .Select(playlist => new PlaylistDto
            {
                Id = playlist.PlaylistId,
                Name = playlist.Title
            })
            .ToArray();
    }
}
