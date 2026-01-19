using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using YouTubester.Abstractions.Users;
using YouTubester.Domain;
using YouTubester.Persistence.Analytics;
using YouTubester.Persistence.Users;
using Channel = YouTubester.Domain.Channel;

namespace YouTubester.Persistence;

public class YouTubesterDb(DbContextOptions<YouTubesterDb> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<UserToken> UserTokens => Set<UserToken>();
    public DbSet<Reply> Replies => Set<Reply>();
    public DbSet<Channel> Channels => Set<Channel>();
    public DbSet<Video> Videos => Set<Video>();
    public DbSet<Playlist> Playlists => Set<Playlist>();
    public DbSet<VideoPlaylist> VideoPlaylists => Set<VideoPlaylist>();
    public DbSet<UserEvent> UserEvents => Set<UserEvent>();


    protected override void OnModelCreating(ModelBuilder b)
    {
        //todo indexes
        b.Entity<Reply>().HasKey(reply => reply.CommentId);
        b.Entity<Reply>().HasIndex(reply => reply.VideoId);
        b.Entity<Reply>().Property(reply => reply.PulledAt);
        b.Entity<Reply>().Property(reply => reply.PostedAt);

        b.Entity<User>().HasKey(user => user.Id);
        b.Entity<User>().Property(user => user.CreatedAt);
        b.Entity<User>().Property(user => user.LastLoginAt);
        b.Entity<UserToken>().HasKey(token => token.UserId);
        b.Entity<UserToken>().Property(token => token.ExpiresAt);
        b.Entity<UserToken>()
            .HasOne<User>()
            .WithOne()
            .HasForeignKey<UserToken>(token => token.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        b.Entity<Channel>().HasKey(channel => channel.ChannelId);
        b.Entity<Channel>().Property(channel => channel.ETag).HasMaxLength(128);
        b.Entity<Channel>().Property(channel => channel.UpdatedAt);
        b.Entity<Channel>().Property(channel => channel.LastUploadsCutoff);
        b.Entity<Video>().HasKey(video => video.VideoId);
        b.Entity<Video>().HasIndex(video => video.UpdatedAt);
        // Composite index for video listing performance (PublishedAt DESC, VideoId DESC)
        b.Entity<Video>().HasIndex(video => new { video.PublishedAt, video.VideoId });
        b.Entity<Video>().Property(video => video.ETag).HasMaxLength(128);
        b.Entity<Video>().Property(video => video.CachedAt);
        b.Entity<Video>().Property(video => video.UpdatedAt);
        b.Entity<Video>().Property(video => video.PublishedAt);
        b.Entity<Video>()
            .OwnsOne(video => video.Location, ownedNavigationBuilder =>
            {
                ownedNavigationBuilder.Property(location => location.Latitude);
                ownedNavigationBuilder.Property(location => location.Longitude);
                ownedNavigationBuilder.WithOwner();
            });

        b.Entity<Playlist>().HasKey(playlist => playlist.PlaylistId);
        b.Entity<Playlist>().HasIndex(playlist => playlist.ChannelId);
        b.Entity<Playlist>().Property(playlist => playlist.ETag).HasMaxLength(128);
        b.Entity<Playlist>().HasOne<Channel>().WithMany().HasForeignKey(playlist => playlist.ChannelId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Entity<Playlist>().Property(playlist => playlist.UpdatedAt);
        b.Entity<Playlist>().Property(playlist => playlist.LastMembershipSyncAt);

        b.Entity<VideoPlaylist>().HasKey(videoPlaylist => new { videoPlaylist.VideoId, videoPlaylist.PlaylistId });
        b.Entity<VideoPlaylist>().HasIndex(videoPlaylist => videoPlaylist.PlaylistId);
        b.Entity<VideoPlaylist>().HasOne<Playlist>().WithMany().HasForeignKey(videoPlaylist => videoPlaylist.PlaylistId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Entity<VideoPlaylist>().HasOne<Video>().WithMany()
            .HasForeignKey(videoPlaylist => videoPlaylist.VideoId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<UserEvent>(entity =>
        {
            entity.ToTable("UserEvents", "analytics");

            entity.HasKey(userEvent => userEvent.Id);

            entity.Property(userEvent => userEvent.Id)
                .ValueGeneratedOnAdd();

            entity.Property(userEvent => userEvent.UserId)
                .IsRequired();

            entity.Property(userEvent => userEvent.OccurredAtUtc)
                .IsRequired();

            entity.Property(userEvent => userEvent.EventType)
                .IsRequired();

            entity.HasIndex(userEvent => new { userEvent.UserId, userEvent.OccurredAtUtc })
                .HasDatabaseName("IX_UserEvents_UserId_OccurredAtUtc_Desc");
            entity.HasIndex(userEvent => new { userEvent.EventType, userEvent.OccurredAtUtc })
                .HasDatabaseName("IX_UserEvents_EventType_OccurredAtUtc_Desc");
            entity.HasIndex(userEvent => userEvent.OccurredAtUtc)
                .HasDatabaseName("IX_UserEvents_OccurredAtUtc_Desc");
        });
    }
}