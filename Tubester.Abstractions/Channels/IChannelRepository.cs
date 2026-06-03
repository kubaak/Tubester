using Tubester.Domain;

namespace Tubester.Abstractions.Channels;

/// <summary>
/// Provides data access operations for channels.
/// </summary>
public interface IChannelRepository
{
    /// <summary>
    /// Gets a channel by its identifier.
    /// </summary>
    /// <param name="channelId">Identifier of the channel.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The channel if found, otherwise null.</returns>
    Task<Channel?> GetChannelAsync(string channelId, CancellationToken cancellationToken);

    /// <summary>
    /// Sets the uploads cutoff moment for the specified channel.
    /// </summary>
    /// <param name="channelId">Identifier of the channel.</param>
    /// <param name="cutoff">Moment after which uploads are considered new.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    Task SetUploadsCutoffAsync(string channelId, DateTimeOffset cutoff, CancellationToken cancellationToken);

    /// <summary>
    /// Inserts or updates the specified channel in the data store.
    /// </summary>
    /// <param name="channel">Channel to persist.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    Task UpsertChannelAsync(Channel channel, CancellationToken cancellationToken);

    /// <summary>
    /// Atomically acquires the comment scan lock for the specified channel.
    /// Returns true if the lock was acquired, false if a scan is already running.
    /// </summary>
    Task<bool> TryAcquireCommentScanLockAsync(string channelId, CancellationToken cancellationToken);

    /// <summary>
    /// Releases the comment scan lock for the specified channel.
    /// </summary>
    Task ReleaseCommentScanLockAsync(string channelId, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes all channels belonging to a user.
    /// This also cascades to videos, replies, and video playlists through FK constraints.
    /// </summary>
    /// <param name="userId">The user ID whose channels should be deleted.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The number of channels deleted.</returns>
    Task<int> DeleteByUserIdAsync(string userId, CancellationToken cancellationToken);

    /// <summary>
    /// Gets all channel IDs for a user.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>List of channel IDs.</returns>
    Task<List<string>> GetChannelIdsByUserIdAsync(string userId, CancellationToken cancellationToken);
}
