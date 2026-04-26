using Microsoft.EntityFrameworkCore;
using Tubester.Abstractions.Users;
using Tubester.Domain;
using Tubester.Persistence.Analytics;
using Tubester.Persistence.Credits;
using Tubester.Persistence.Users;
using Channel = Tubester.Domain.Channel;

namespace Tubester.Persistence;

public class TubesterDb(DbContextOptions<TubesterDb> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<UserToken> UserTokens => Set<UserToken>();
    public DbSet<Reply> Replies => Set<Reply>();
    public DbSet<Channel> Channels => Set<Channel>();
    public DbSet<Video> Videos => Set<Video>();
    public DbSet<Playlist> Playlists => Set<Playlist>();
    public DbSet<VideoPlaylist> VideoPlaylists => Set<VideoPlaylist>();
    public DbSet<UserEvent> UserEvents => Set<UserEvent>();

    public DbSet<Plan> Plans => Set<Plan>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<Wallet> Wallets => Set<Wallet>();
    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();
    public DbSet<ActionCost> ActionCosts => Set<ActionCost>();
    public DbSet<ChannelSettings> ChannelSettings => Set<ChannelSettings>();
    public DbSet<AccountSettings> AccountSettings => Set<AccountSettings>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        //todo indexes
        b.Entity<Reply>().HasKey(reply => reply.CommentId);
        b.Entity<Reply>().HasIndex(reply => reply.VideoId);
        b.Entity<Reply>().HasIndex(reply => new { reply.OriginalCommentAt, reply.CommentId });
        b.Entity<Reply>().Property(reply => reply.OriginalCommentAt);
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
        b.Entity<Channel>().Property(channel => channel.IsCommentScanRunning);
        b.Entity<Channel>().HasIndex(channel => channel.UserId);
        b.Entity<Channel>()
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(channel => channel.UserId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();
        b.Entity<Video>()
            .HasOne<Channel>()
            .WithMany()
            .HasForeignKey(v => v.UploadsPlaylistId)
            .HasPrincipalKey(c => c.UploadsPlaylistId);

        b.Entity<ChannelSettings>(entity =>
        {
            entity.HasKey(channelSettings => channelSettings.ChannelId);

            entity.HasIndex(channelSettings => channelSettings.ChannelId)
                .IsUnique();

            entity.HasOne<Channel>()
                .WithOne()
                .HasForeignKey<ChannelSettings>(channelSettings => channelSettings.ChannelId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Property(channelSettings => channelSettings.ReplyLanguage)
                .IsRequired()
                .HasMaxLength(50);

            entity.Property(channelSettings => channelSettings.ResponseForNonTextualComments)
                .HasMaxLength(500);

            entity.Property(channelSettings => channelSettings.UpdatedAtUtc)
                .IsRequired();
        });
        b.Entity<AccountSettings>(entity =>
        {
            entity.HasKey(s => s.UserId);

            entity.HasOne<User>()
                .WithOne()
                .HasForeignKey<AccountSettings>(s => s.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Property(s => s.PreferredTheme)
                .IsRequired()
                .HasMaxLength(50);

            entity.Property(s => s.PreferredLanguage)
                .IsRequired()
                .HasMaxLength(20);

            entity.Property(s => s.UpdatedAtUtc)
                .IsRequired();
        });
        b.Entity<Playlist>()
            .HasOne<Channel>()
            .WithMany()
            .HasForeignKey(p => p.ChannelId);

        b.Entity<Video>().HasKey(video => video.VideoId);
        b.Entity<Video>().HasIndex(video => video.UpdatedAt);
        // Composite index for video listing performance (PublishedAt DESC, VideoId DESC)
        b.Entity<Video>().HasIndex(video => new { video.PublishedAt, video.VideoId });
        b.Entity<Video>().Property(video => video.ETag).HasMaxLength(128);
        b.Entity<Video>().Property(video => video.CachedAt);
        b.Entity<Video>().Property(video => video.UpdatedAt);
        b.Entity<Video>().Property(video => video.PublishedAt);
        b.Entity<Video>().Property(video => video.Visibility).HasConversion<string>().HasMaxLength(10).IsRequired();

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
        b.Entity<Playlist>().Property(playlist => playlist.Visibility).HasConversion<string>().HasMaxLength(10).IsRequired();
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
        b.Entity<Plan>(entity =>
        {
            entity.ToTable("Plans");

            entity.HasKey(plan => plan.Id);

            entity.Property(plan => plan.Code)
                .IsRequired();

            entity.Property(plan => plan.Name)
                .IsRequired();

            entity.Property(plan => plan.MonthlyCredits)
                .IsRequired();

            entity.Property(plan => plan.IsActive)
                .IsRequired();

            entity.Property(plan => plan.CreatedAtUtc)
                .IsRequired();

            entity.Property(plan => plan.UpdatedAtUtc)
                .IsRequired();

            entity.HasIndex(plan => plan.Code)
                .IsUnique();
        });

        b.Entity<Subscription>(entity =>
        {
            entity.ToTable("Subscriptions");

            entity.HasKey(subscription => subscription.UserId);

            entity.Property(subscription => subscription.PlanId)
                .IsRequired();

            entity.Property(subscription => subscription.PeriodStartUtc)
                .IsRequired();

            entity.Property(subscription => subscription.PeriodEndUtc)
                .IsRequired();

            entity.Property(subscription => subscription.Status)
                .IsRequired()
                .HasConversion<string>();

            entity.HasOne(subscription => subscription.Plan)
                .WithMany()
                .HasForeignKey(subscription => subscription.PlanId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Wallet>(entity =>
        {
            entity.ToTable("Wallets");

            entity.HasKey(wallet => wallet.UserId);

            entity.Property(wallet => wallet.Balance)
                .IsRequired();

            entity.Property(wallet => wallet.PeriodStartUtc)
                .IsRequired();

            entity.Property(wallet => wallet.PeriodEndUtc)
                .IsRequired();

            entity.Property(wallet => wallet.UpdatedAtUtc)
                .IsRequired();

            entity.HasOne<User>()
                .WithOne()
                .HasForeignKey<Wallet>(wallet => wallet.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<LedgerEntry>(entity =>
        {
            entity.ToTable("LedgerEntries");

            entity.HasKey(entry => entry.Id);

            entity.Property(entry => entry.Id)
                .ValueGeneratedOnAdd();

            entity.Property(entry => entry.UserId)
                .IsRequired();

            entity.Property(entry => entry.OccurredAtUtc)
                .IsRequired();

            entity.Property(entry => entry.ActionType)
                .IsRequired();

            entity.Property(entry => entry.Delta)
                .IsRequired();

            entity.Property(entry => entry.IdempotencyKey)
                .IsRequired();

            entity.HasIndex(entry => new { entry.UserId, entry.IdempotencyKey })
                .IsUnique();

            entity.HasIndex(entry => new { entry.UserId, entry.OccurredAtUtc })
                .HasDatabaseName("IX_LedgerEntries_UserId_OccurredAtUtc_Desc");

            entity.HasIndex(entry => new { entry.ActionType, entry.OccurredAtUtc })
                .HasDatabaseName("IX_LedgerEntries_ActionType_OccurredAtUtc_Desc");
        });

        b.Entity<ActionCost>(entity =>
        {
            entity.ToTable("ActionCosts");

            entity.HasKey(cost => cost.ActionType);

            entity.Property(cost => cost.Cost)
                .IsRequired();

            entity.Property(cost => cost.IsEnabled)
                .IsRequired();

            entity.Property(cost => cost.UpdatedAtUtc)
                .IsRequired();

            entity.HasIndex(cost => cost.IsEnabled);
        });

        //Domain events are not meant to be persisted
        foreach (var entityType in b.Model.GetEntityTypes())
        {
            if (typeof(Entity).IsAssignableFrom(entityType.ClrType))
            {
                b.Entity(entityType.ClrType).Ignore(nameof(Entity.DomainEvents));
            }
        }
    }
}
