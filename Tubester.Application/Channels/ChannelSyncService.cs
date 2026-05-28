using Microsoft.Extensions.Logging;
using Tubester.Abstractions;
using Tubester.Abstractions.Channels;
using Tubester.Abstractions.Credits;
using Tubester.Abstractions.Playlists;
using Tubester.Abstractions.Videos;
using Tubester.Application.Common;
using Tubester.Domain;
using Tubester.Integration;
using static Tubester.Application.Common.PlaylistVisibilityMapper;

namespace Tubester.Application.Channels;

// todos
// 1) Short-circuit unchanged playlists via stored ETags
// 2) Use transactions
public sealed class ChannelSyncService(
    IPlaylistRepository playlistRepository,
    IYouTubeIntegration youTubeIntegration,
    IVideoRepository videoRepository,
    IChannelRepository channelRepository,
    IChannelSettingsRepository channelSettingsRepository,
    ICurrentChannelContext channelContext,
    ICreditsStore creditsStore,
    ILogger<ChannelSyncService> logger,
    IDateTimeOffsetProvider dateTimeOffsetProvider) : IChannelSyncService
{
    private const int VideoBatchSize = 100;

    public async Task<Channel> PullChannelAsync(
        string userId,
        string channelId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("User id is required.", nameof(userId));
        }

        if (string.IsNullOrWhiteSpace(channelId))
        {
            throw new ArgumentException("Channel id is required.", nameof(channelId));
        }

        // Pull canonical channel details (ChannelId, Title, UploadsPlaylistId, ETag)
        var channelDto = await youTubeIntegration.GetChannelAsync(channelId, cancellationToken)
                         ?? throw new NotFoundException($"Channel '{channelId}' not found on YouTube.");

        var now = dateTimeOffsetProvider.GetUtcNowDateTimeOffset();

        var existingChannel = await channelRepository.GetChannelAsync(channelDto.Id, cancellationToken);

        if (existingChannel is null)
        {
            var channel = Channel.Create(
                channelDto.Id,
                userId,
                channelDto.Name,
                channelDto.UploadsPlaylistId,
                now,
                null,
                channelDto.ETag);

            await channelRepository.UpsertChannelAsync(channel, cancellationToken);
            return channel;
        }

        // Apply remote snapshot via domain behavior; persist only if dirty.
        var changed = existingChannel.ApplyRemoteSnapshot(
            channelDto.Name,
            channelDto.UploadsPlaylistId,
            channelDto.ETag,
            now);

        if (changed)
        {
            await channelRepository.UpsertChannelAsync(existingChannel, cancellationToken);
        }

        return existingChannel;
    }

    public async Task<ChannelSyncResult?> SyncChannelAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        var nowUtc = dateTimeOffsetProvider.GetUtcNowDateTimeOffset();
        var subscription = await creditsStore.GetUserSubscriptionAsync(userId, cancellationToken);

        if (subscription is null)
        {
            await creditsStore.AssignFreeSubscriptionAsync(userId, nowUtc, cancellationToken);
        }
        else if (!subscription.IsActive)
        {
            logger.LogWarning("User {UserId} has an inactive subscription; sync aborted", userId);
            return null;
        }

        var channelId = channelContext.GetRequiredChannelId();

        var channel = await channelRepository.GetChannelAsync(channelId, cancellationToken)
                      ?? await PullChannelAsync(userId, channelId, cancellationToken);

        var settings = await channelSettingsRepository.GetByChannelIdAsync(channelId, cancellationToken);

        if (settings is null)
        {
            settings = ChannelSettings.CreateDefault(channelId, nowUtc);
            await channelSettingsRepository.UpsertAsync(settings, cancellationToken);
        }

        return await SyncInternalAsync(channel, nowUtc, cancellationToken);
    }

    private async Task<ChannelSyncResult> SyncInternalAsync(
        Channel channel,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Starting playlist sync for channel {ChannelId}", channel.ChannelId);

        var (videosInserted, videosUpdated, synchronizedVideoIds) =
            await SyncUploadsAsync(channel, now, cancellationToken);

        var (playlistsInserted, playlistsUpdated, membershipsAdded, membershipsRemoved) =
            await SyncPlaylistMembershipsAsync(synchronizedVideoIds, channel, now, cancellationToken);

        var result = new ChannelSyncResult(
            videosInserted,
            videosUpdated,
            playlistsInserted,
            playlistsUpdated,
            membershipsAdded,
            membershipsRemoved);

        logger.LogInformation(
            "Playlist sync completed for channel {ChannelId}. Videos: {VideosInserted} inserted, {VideosUpdated} updated. " +
            "Playlists: {PlaylistsInserted} inserted, {PlaylistsUpdated} updated. Memberships: {MembershipsAdded} added, {MembershipsRemoved} removed",
            channel.ChannelId,
            result.VideosInserted,
            result.VideosUpdated,
            result.PlaylistsInserted,
            result.PlaylistsUpdated,
            result.MembershipsAdded,
            result.MembershipsRemoved);

        return result;
    }

    private async Task<(int videosInserted, int videosUpdated, HashSet<string> synchronizedVideoIds)> SyncUploadsAsync(
        Channel channel,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var channelId = channel.ChannelId;
        var uploadsPlaylistId = channel.UploadsPlaylistId;
        var cutoff = channel.LastUploadsCutoff;

        logger.LogInformation(
            "Executing uploads delta sync for channel {ChannelId} with cutoff {Cutoff}",
            channelId,
            cutoff);

        var totalVideosInserted = 0;
        var totalVideosUpdated = 0;
        var totalDirtyVideosSkipped = 0;

        var batch = new List<Video>(VideoBatchSize);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var synchronizedVideoIds = new HashSet<string>(StringComparer.Ordinal);

        var maxPublishedAt = cutoff ?? DateTimeOffset.MinValue;
        var processedAny = false;

        await foreach (var videoDto in youTubeIntegration.GetAllVideosAsync(
                           uploadsPlaylistId,
                           cutoff,
                           cancellationToken))
        {
            if (!seen.Add(videoDto.VideoId))
            {
                continue;
            }

            processedAny = true;

            if (videoDto.PublishedAt > maxPublishedAt)
            {
                maxPublishedAt = videoDto.PublishedAt;
            }

            var visibility = VideoVisibilityMapper.MapVisibility(
                videoDto.PrivacyStatus,
                videoDto.PublishedAt,
                now);

            batch.Add(Video.Create(
                uploadsPlaylistId,
                videoDto.VideoId,
                videoDto.Title,
                videoDto.Description,
                videoDto.PublishedAt,
                videoDto.Duration,
                visibility,
                videoDto.Tags,
                videoDto.CategoryId,
                videoDto.DefaultLanguage,
                videoDto.DefaultAudioLanguage,
                now,
                videoDto.ETag));

            if (batch.Count >= VideoBatchSize)
            {
                await FlushBatchAsync();
            }
        }

        await FlushBatchAsync();

        if (totalDirtyVideosSkipped > 0)
        {
            logger.LogInformation(
                "Skipped {SkippedDirtyVideos} dirty videos during uploads sync for channel {ChannelId}",
                totalDirtyVideosSkipped,
                channelId);
        }

        if (!processedAny || (cutoff.HasValue && maxPublishedAt <= cutoff.Value))
        {
            return (totalVideosInserted, totalVideosUpdated, synchronizedVideoIds);
        }

        await channelRepository.SetUploadsCutoffAsync(channelId, maxPublishedAt, cancellationToken);

        logger.LogDebug(
            "Updated uploads cutoff to {Cutoff} for channel {ChannelId}",
            maxPublishedAt,
            channelId);

        return (totalVideosInserted, totalVideosUpdated, synchronizedVideoIds);

        async Task FlushBatchAsync()
        {
            if (batch.Count == 0)
            {
                return;
            }

            var batchCount = batch.Count;

            var (inserted, updated, syncedVideoIds) =
                await videoRepository.UpsertRemoteSyncAsync(
                    uploadsPlaylistId, batch, cancellationToken);

            totalVideosInserted += inserted;
            totalVideosUpdated += updated;

            foreach (var videoId in syncedVideoIds)
            {
                synchronizedVideoIds.Add(videoId);
            }

            totalDirtyVideosSkipped += batchCount - syncedVideoIds.Count;

            batch.Clear();
        }
    }

    private async Task<(int PlaylistsInserted, int PlaylistsUpdated, int MembershipsAdded, int MembershipsRemoved)>
        SyncPlaylistMembershipsAsync(
            HashSet<string> synchronizedVideoIds,
            Channel channel,
            DateTimeOffset now,
            CancellationToken cancellationToken)
    {
        if (synchronizedVideoIds.Count == 0)
        {
            return (0, 0, 0, 0);
        }

        var channelId = channel.ChannelId;
        var totalMembershipsAdded = 0;
        var totalMembershipsRemoved = 0;

        var remotePlaylists = new List<Playlist>();

        await foreach (var dto in youTubeIntegration.GetPlaylistsAsync(channelId, cancellationToken))
        {
            if (string.IsNullOrWhiteSpace(dto.Id))
            {
                continue;
            }

            remotePlaylists.Add(Playlist.Create(
                dto.Id,
                channelId,
                dto.Title,
                dto.Description,
                MapVisibility(dto.Visibility, logger),
                now,
                dto.ETag));
        }

        if (remotePlaylists.Count == 0)
        {
            return (0, 0, 0, 0);
        }

        var (totalPlaylistsInserted, totalPlaylistsUpdated) =
            await playlistRepository.UpsertAsync(remotePlaylists, cancellationToken);

        foreach (var playlist in remotePlaylists)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var remotePlaylistVideoIds = new HashSet<string>(StringComparer.Ordinal);

            await foreach (var videoId in youTubeIntegration.GetPlaylistVideoIdsAsync(
                               playlist.PlaylistId,
                               cancellationToken))
            {
                if (synchronizedVideoIds.Contains(videoId))
                {
                    remotePlaylistVideoIds.Add(videoId);
                }
            }

            var localPlaylistVideoIds =
                await playlistRepository.GetMembershipVideoIdsAsync(playlist.PlaylistId, cancellationToken);

            var synchronizedLocalVideoIds = localPlaylistVideoIds
                .Where(synchronizedVideoIds.Contains)
                .ToHashSet(StringComparer.Ordinal);

            var toAdd = remotePlaylistVideoIds.ToHashSet(StringComparer.Ordinal);
            var toRemove = synchronizedLocalVideoIds.Except(remotePlaylistVideoIds).ToList();

            if (toAdd.Count > 0)
            {
                // Only add memberships for videos that are already known uploads for this channel.
                // This prevents importing videos that belong to other channels but are present in the user's playlists.
                var existingVideosById = await videoRepository.GetVideoETagsAsync(
                    channel.UploadsPlaylistId,
                    toAdd,
                    cancellationToken);

                var knownVideoIds = toAdd
                    .Where(existingVideosById.ContainsKey)
                    .ToHashSet(StringComparer.Ordinal);

                if (knownVideoIds.Count > 0)
                {
                    totalMembershipsAdded += await playlistRepository.AddMembershipsAsync(
                        playlist.PlaylistId,
                        knownVideoIds,
                        cancellationToken);
                }
            }

            if (toRemove.Count > 0)
            {
                totalMembershipsRemoved += await playlistRepository.RemoveMembershipsAsync(
                    playlist.PlaylistId,
                    toRemove,
                    cancellationToken);
            }

            await playlistRepository.UpdateLastMembershipSyncAtAsync(
                playlist.PlaylistId,
                now,
                cancellationToken);
        }

        return (
            totalPlaylistsInserted,
            totalPlaylistsUpdated,
            totalMembershipsAdded,
            totalMembershipsRemoved);
    }
}