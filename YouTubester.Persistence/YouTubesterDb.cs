using Microsoft.EntityFrameworkCore;
using YouTubester.Abstractions.Users;
using YouTubester.Domain;
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

    protected override void OnModelCreating(ModelBuilder b)
    {
        //todo indexes
        b.Entity<Reply>().HasKey(x => x.CommentId);
        b.Entity<Reply>().HasIndex(x => x.VideoId);
        //.HasConversion because Sqlite doesn't support DateTimeOffset
        b.Entity<Reply>().Property(x => x.PulledAt).HasConversion(
            v => v.UtcDateTime,
            v => new DateTimeOffset(v, TimeSpan.Zero));
        b.Entity<Reply>().Property(x => x.PostedAt).HasConversion(
            v => !v.HasValue ? (DateTime?)null : v.Value.UtcDateTime,
            v => v.HasValue ? new DateTimeOffset(v.Value, TimeSpan.Zero) : null);

        b.Entity<User>().HasKey(x => x.Id);
        b.Entity<User>().Property(x => x.CreatedAt).HasConversion(
            v => v.UtcDateTime,
            v => new DateTimeOffset(v, TimeSpan.Zero));
        b.Entity<User>().Property(x => x.LastLoginAt).HasConversion(
            v => !v.HasValue ? (DateTime?)null : v.Value.UtcDateTime,
            v => v.HasValue ? new DateTimeOffset(v.Value, TimeSpan.Zero) : null);

        b.Entity<UserToken>().HasKey(x => x.UserId);
        b.Entity<UserToken>().Property(x => x.ExpiresAt).HasConversion(
            v => !v.HasValue ? (DateTime?)null : v.Value.UtcDateTime,
            v => v.HasValue ? new DateTimeOffset(v.Value, TimeSpan.Zero) : null);
        b.Entity<UserToken>()
            .HasOne<User>()
            .WithOne()
            .HasForeignKey<UserToken>(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        b.Entity<Channel>().HasKey(x => x.ChannelId);
        b.Entity<Channel>().Property(x => x.UserId).IsRequired();
        b.Entity<Channel>().Property(x => x.ETag).HasMaxLength(128);
        b.Entity<Channel>().Property(x => x.UpdatedAt).HasConversion(
            v => v.UtcDateTime,
            v => new DateTimeOffset(v, TimeSpan.Zero));
        b.Entity<Channel>().Property(x => x.LastUploadsCutoff).HasConversion(
            v => !v.HasValue ? (DateTime?)null : v.Value.UtcDateTime,
            v => v.HasValue ? new DateTimeOffset(v.Value, TimeSpan.Zero) : null);
        //todo
        // b.Entity<Channel>()
        //     .HasOne<User>()
        //     .WithMany()
        //     .HasForeignKey(x => x.UserId)
        //     .OnDelete(DeleteBehavior.Cascade);

        b.Entity<Video>().HasKey(v => v.VideoId);
        b.Entity<Video>().HasIndex(x => x.UpdatedAt);
        // Composite index for video listing performance (PublishedAt DESC, VideoId DESC)
        b.Entity<Video>().HasIndex(v => new { v.PublishedAt, v.VideoId });
        b.Entity<Video>().Property(x => x.ETag).HasMaxLength(128);
        //.HasConversion because Sqlite doesn't support DateTimeOffset
        b.Entity<Video>().Property(x => x.CachedAt).HasConversion(
            v => v.UtcDateTime,
            v => new DateTimeOffset(v, TimeSpan.Zero));
        b.Entity<Video>().Property(x => x.UpdatedAt).HasConversion(
            v => v.UtcDateTime,
            v => new DateTimeOffset(v, TimeSpan.Zero));
        b.Entity<Video>().Property(x => x.PublishedAt).HasConversion(
            v => v.UtcDateTime,
            v => new DateTimeOffset(v, TimeSpan.Zero));
        b.Entity<Video>()
            .OwnsOne(v => v.Location, x =>
            {
                x.Property(p => p.Latitude);
                x.Property(p => p.Longitude);
                x.WithOwner();
            });

        b.Entity<Playlist>().HasKey(x => x.PlaylistId);
        b.Entity<Playlist>().HasIndex(x => x.ChannelId);
        b.Entity<Playlist>().Property(x => x.ETag).HasMaxLength(128);
        b.Entity<Playlist>().HasOne<Channel>().WithMany().HasForeignKey(p => p.ChannelId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Entity<Playlist>().Property(x => x.UpdatedAt).HasConversion(
            v => v.UtcDateTime,
            v => new DateTimeOffset(v, TimeSpan.Zero));
        b.Entity<Playlist>().Property(x => x.LastMembershipSyncAt).HasConversion(
            v => !v.HasValue ? (DateTime?)null : v.Value.UtcDateTime,
            v => v.HasValue ? new DateTimeOffset(v.Value, TimeSpan.Zero) : null);

        b.Entity<VideoPlaylist>().HasKey(x => new { x.VideoId, x.PlaylistId });
        b.Entity<VideoPlaylist>().HasIndex(x => x.PlaylistId);
        b.Entity<VideoPlaylist>().HasOne<Playlist>().WithMany().HasForeignKey(x => x.PlaylistId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Entity<VideoPlaylist>().HasOne<Video>().WithMany()
            .HasForeignKey(x => x.VideoId).OnDelete(DeleteBehavior.Cascade);
    }
}