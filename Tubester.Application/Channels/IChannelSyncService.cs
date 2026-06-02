using Tubester.Domain;

namespace Tubester.Application.Channels;

public interface IChannelSyncService
{
    /// <summary>
    /// Synchronizes the current channel for the specified user based on the current channel context.
    /// Returns null if the user has a subscription that is not active.
    /// If the user has no subscription, a free subscription is assigned automatically.
    /// </summary>
    Task<ChannelSyncResult?> SyncChannelAsync(string userId, CancellationToken cancellationToken);

    /// <summary>
    /// Pulls the channel details for the specified user and channel ID.
    /// </summary>
    /// <param name="userId"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task<Channel> PullChannelAsync(
        string userId,
        CancellationToken cancellationToken);
}