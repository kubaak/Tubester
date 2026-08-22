namespace Tubester.Application.Channels;

public interface IChannelSyncService
{
    /// <summary>
    /// Synchronizes the current channel for the specified user based on the current channel context.
    /// Returns null if the user has a subscription that is not active.
    /// If the user has no subscription, a free subscription is assigned automatically.
    /// </summary>
    Task<ChannelSyncResult?> SyncChannelAsync(string userId, CancellationToken cancellationToken);
}