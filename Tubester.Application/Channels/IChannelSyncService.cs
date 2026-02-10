using Tubester.Abstractions.Channels;
using Tubester.Domain;

namespace Tubester.Application.Channels;

public interface IChannelSyncService
{
    /// <summary>
    /// Pulls channel metadata from YouTube and persists a canonical Channel aggregate for the given user.
    /// - Creates a new row if it does not exist.
    /// - Otherwise applies a remote snapshot (Name, UploadsPlaylistId, ETag) and updates only if changed.
    /// Returns the up-to-date aggregate.
    /// </summary>
    Task<Channel> PullChannelAsync(string userId, string channelId, CancellationToken cancellationToken);

    /// <summary>
    /// Synchronizes the current channel for the specified user based on the current channel context.
    /// </summary>
    Task<ChannelSyncResult> SyncChannelAsync(string userId, CancellationToken cancellationToken);

    /// <summary>
    /// Returns all YouTube channels available to pull for the specified user.
    /// </summary>
    Task<IReadOnlyList<ChannelDto>> GetAvailableYoutubeChannelsForUserAsync(
        CancellationToken cancellationToken);
}