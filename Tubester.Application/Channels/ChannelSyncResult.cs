namespace Tubester.Application.Channels;

public sealed record ChannelSyncResult(
    int VideosInserted,
    int VideosUpdated,
    int PlaylistsInserted,
    int PlaylistsUpdated,
    int MembershipsAdded,
    int MembershipsRemoved);