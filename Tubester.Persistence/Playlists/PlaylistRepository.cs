using Microsoft.EntityFrameworkCore;
using Tubester.Abstractions.Playlists;
using Tubester.Domain;

namespace Tubester.Persistence.Playlists;

public sealed class PlaylistRepository(TubesterDb databaseContext) : IPlaylistRepository
{
    public async Task<List<Playlist>> GetByChannelAsync(string channelId, CancellationToken cancellationToken)
    {
        return await databaseContext.Playlists
            .AsNoTracking()
            .Where(p => p.ChannelId == channelId)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<PlaylistCandidateDto>> GetPublicByChannelAsync(string channelId, CancellationToken cancellationToken)
    {
        return await databaseContext.Playlists
            .AsNoTracking()
            .Where(p => p.ChannelId == channelId && p.Visibility == PlaylistVisibility.Public)
            .Select(p => new PlaylistCandidateDto
            {
                PlaylistId = p.PlaylistId,
                Name = p.Title ?? string.Empty,
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<List<string>> GetPlaylistNamesForLatestPublicVideoAsync(
        string channelId,
        CancellationToken cancellationToken)
    {
        var latestVideoIdQuery = databaseContext.Videos
            .AsNoTracking()
            .Join(
                databaseContext.Channels.AsNoTracking(),
                v => v.UploadsPlaylistId,
                c => c.UploadsPlaylistId,
                (v, c) => new { v, c })
            .Where(x => x.c.ChannelId == channelId)
            .Where(x => x.v.Visibility == VideoVisibility.Public)
            .OrderByDescending(x => x.v.PublishedAt)
            .Select(x => x.v.VideoId)
            .Take(1);

        return await databaseContext.VideoPlaylists
            .AsNoTracking()
            .Where(vp => latestVideoIdQuery.Contains(vp.VideoId))
            .Join(
                databaseContext.Playlists.AsNoTracking(),
                vp => vp.PlaylistId,
                p => p.PlaylistId,
                (vp, p) => p.Title ?? string.Empty)
            .Distinct()
            .ToListAsync(cancellationToken);
    }
    public Task<List<string>> GetPlaylistIdsByVideoAsync(string videoId, CancellationToken cancellationToken)
    {
        return databaseContext.VideoPlaylists
            .AsNoTracking()
            .Where(v => v.VideoId == videoId)
            .Select(v => v.PlaylistId)
            .ToListAsync(cancellationToken);
    }

    public Task<List<Playlist>> GetPlaylistsByVideoAsync(string videoId, CancellationToken cancellationToken)
    {
        return databaseContext.VideoPlaylists
            .AsNoTracking()
            .Where(vp => vp.VideoId == videoId)
            .Join(databaseContext.Playlists,
                vp => vp.PlaylistId,
                p => p.PlaylistId,
                (_, p) => p)
            .ToListAsync(cancellationToken);
    }

    public async Task<Playlist?> GetAsync(string playlistId, CancellationToken cancellationToken)
    {
        return await databaseContext.Playlists
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.PlaylistId == playlistId, cancellationToken);
    }

    public async Task<(int inserted, int updated)> UpsertAsync(IEnumerable<Playlist> playlists,
        CancellationToken cancellationToken)
    {
        var playlistList = playlists.ToList();
        if (playlistList.Count == 0)
        {
            return (0, 0);
        }

        var playlistIds = playlistList.Select(p => p.PlaylistId).ToHashSet();

        var existingPlaylists = await databaseContext.Playlists
            .Where(p => playlistIds.Contains(p.PlaylistId))
            .ToDictionaryAsync(p => p.PlaylistId, p => p, cancellationToken);

        var currentTime = DateTimeOffset.UtcNow;
        var insertedCount = 0;
        var updatedCount = 0;

        foreach (var playlist in playlistList)
        {
            if (!existingPlaylists.TryGetValue(playlist.PlaylistId, out var existingPlaylist))
            {
                databaseContext.Playlists.Add(playlist);
                insertedCount++;
            }
            else
            {
                existingPlaylist.UpdateTitle(playlist.Title, playlist.Description, playlist.Visibility, currentTime, playlist.ETag);
                existingPlaylist.TransferEventsFrom(playlist);
                updatedCount++;
            }
        }

        await databaseContext.SaveChangesAsync(cancellationToken);
        return (insertedCount, updatedCount);
    }

    public async Task<HashSet<string>> GetMembershipVideoIdsAsync(string playlistId,
        CancellationToken cancellationToken)
    {
        var videoIds = await databaseContext.VideoPlaylists
            .AsNoTracking()
            .Where(vp => vp.PlaylistId == playlistId)
            .Select(vp => vp.VideoId)
            .ToListAsync(cancellationToken);

        return videoIds.ToHashSet(StringComparer.Ordinal);
    }

    public async Task<int> SetMembershipsToPlaylistsAsync(
        string videoId,
        HashSet<string> playlistIds,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(videoId))
        {
            return 0;
        }

        // FK safety: video must exist
        var videoExists = await databaseContext.Videos
            .AsNoTracking()
            .AnyAsync(v => v.VideoId == videoId, cancellationToken);
        if (!videoExists)
        {
            return 0;
        }

        // Only keep playlists that actually exist (silently ignore unknown IDs)
        var desired = playlistIds.Count == 0
            ? new HashSet<string>(StringComparer.Ordinal) // explicit "clear all"
            : await databaseContext.Playlists
                .AsNoTracking()
                .Where(p => playlistIds.Contains(p.PlaylistId))
                .Select(p => p.PlaylistId)
                .ToHashSetAsync(StringComparer.Ordinal, cancellationToken);

        // Current memberships for this video
        var current = await databaseContext.VideoPlaylists
            .AsNoTracking()
            .Where(vp => vp.VideoId == videoId)
            .Select(vp => vp.PlaylistId)
            .ToHashSetAsync(StringComparer.Ordinal, cancellationToken);

        // Diff
        var toAdd = desired.Except(current).ToList();
        var toRemove = current.Except(desired).ToList();

        if (toAdd.Count == 0 && toRemove.Count == 0)
        {
            return 0;
        }

        await using var tx = await databaseContext.Database.BeginTransactionAsync(cancellationToken);

        // Remove memberships not desired
        var removedCount = 0;
        if (toRemove.Count > 0)
        {
            // Use ExecuteDeleteAsync => single round trip
            removedCount = await databaseContext.VideoPlaylists
                .Where(vp => vp.VideoId == videoId && toRemove.Contains(vp.PlaylistId))
                .ExecuteDeleteAsync(cancellationToken);
        }

        // Add new memberships (batched)
        var addedCount = 0;
        if (toAdd.Count > 0)
        {
            const int batch = 500;
            for (var i = 0; i < toAdd.Count; i += batch)
            {
                var slice = toAdd.Skip(i).Take(batch)
                    .Select(pid => VideoPlaylist.Create(videoId, pid))
                    .ToList();

                if (slice.Count > 0)
                {
                    databaseContext.VideoPlaylists.AddRange(slice);
                    addedCount += slice.Count;
                    await databaseContext.SaveChangesAsync(cancellationToken);
                }
            }
        }

        await tx.CommitAsync(cancellationToken);
        return addedCount + removedCount;
    }

    public async Task<int> AddMembershipsAsync(
        string playlistId,
        HashSet<string> videoIds,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(playlistId))
        {
            return 0;
        }

        if (videoIds.Count == 0)
        {
            return 0;
        }

        // FK safety: ensure the playlist exists
        var playlistExists = await databaseContext.Playlists
            .AsNoTracking()
            .AnyAsync(p => p.PlaylistId == playlistId, cancellationToken);
        if (!playlistExists)
        {
            return 0;
        }

        // Find memberships that already exist for this playlist among requested videos
        var alreadyLinked = await databaseContext.VideoPlaylists
            .AsNoTracking()
            .Where(vp => vp.PlaylistId == playlistId && videoIds.Contains(vp.VideoId))
            .Select(vp => vp.VideoId)
            .ToHashSetAsync(StringComparer.Ordinal, cancellationToken);

        // Only add those not yet linked
        var candidates = videoIds.Except(alreadyLinked).ToList();
        if (candidates.Count == 0)
        {
            return 0;
        }

        // FK safety: keep only videos that actually exist
        var existingVideoIds = await databaseContext.Videos
            .AsNoTracking()
            .Where(v => candidates.Contains(v.VideoId))
            .Select(v => v.VideoId)
            .ToHashSetAsync(StringComparer.Ordinal, cancellationToken);

        if (existingVideoIds.Count == 0)
        {
            return 0;
        }

        // Insert in batches
        const int batchSize = 500;
        var addedCount = 0;
        var toInsert = existingVideoIds.ToList();

        for (var i = 0; i < toInsert.Count; i += batchSize)
        {
            var batch = toInsert
                .Skip(i)
                .Take(batchSize)
                .Select(videoId => VideoPlaylist.Create(videoId, playlistId))
                .ToList();

            if (batch.Count == 0)
            {
                continue;
            }

            databaseContext.VideoPlaylists.AddRange(batch);
            addedCount += batch.Count;
            await databaseContext.SaveChangesAsync(cancellationToken);
        }

        return addedCount;
    }

    public async Task<int> RemoveMembershipsAsync(string playlistId, IEnumerable<string> videoIds,
        CancellationToken cancellationToken)
    {
        var videoIdsToRemove = videoIds.ToHashSet(StringComparer.Ordinal);
        if (videoIdsToRemove.Count == 0)
        {
            return 0;
        }

        var membershipsToRemove = await databaseContext.VideoPlaylists
            .Where(vp => vp.PlaylistId == playlistId && videoIdsToRemove.Contains(vp.VideoId))
            .ToListAsync(cancellationToken);

        if (membershipsToRemove.Count == 0)
        {
            return 0;
        }

        databaseContext.VideoPlaylists.RemoveRange(membershipsToRemove);
        await databaseContext.SaveChangesAsync(cancellationToken);
        return membershipsToRemove.Count;
    }

    public async Task UpdateLastMembershipSyncAtAsync(string playlistId, DateTimeOffset syncedAt,
        CancellationToken cancellationToken)
    {
        var playlist = await databaseContext.Playlists
            .FirstOrDefaultAsync(p => p.PlaylistId == playlistId, cancellationToken);

        if (playlist != null)
        {
            playlist.SetLastMembershipSyncAt(syncedAt);
            await databaseContext.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<Dictionary<string, string?>> GetPlaylistETagsAsync(IEnumerable<string> playlistIds,
        CancellationToken cancellationToken)
    {
        var playlistIdsList = playlistIds.ToList();
        if (playlistIdsList.Count == 0)
        {
            return new Dictionary<string, string?>();
        }

        return await databaseContext.Playlists
            .AsNoTracking()
            .Where(p => playlistIdsList.Contains(p.PlaylistId))
            .ToDictionaryAsync(p => p.PlaylistId, p => p.ETag, cancellationToken);
    }
}