using Hangfire;
using Hangfire.PostgreSql;
using Hangfire.RecurringJobExtensions;
using YouTubester.Abstractions.Analytics;
using YouTubester.Abstractions.Auth;
using YouTubester.Abstractions.Channels;
using YouTubester.Abstractions.Credits;
using YouTubester.Abstractions.Playlists;
using YouTubester.Abstractions.Replies;
using YouTubester.Abstractions.Videos;
using YouTubester.Application;
using YouTubester.Application.Credits;
using YouTubester.Application.Jobs;
using YouTubester.Application.Videos;
using YouTubester.Integration;
using YouTubester.Persistence;
using YouTubester.Persistence.Analytics;
using YouTubester.Persistence.Channels;
using YouTubester.Persistence.Credits;
using YouTubester.Persistence.Playlists;
using YouTubester.Persistence.Replies;
using YouTubester.Persistence.Users;
using YouTubester.Persistence.Videos;

namespace YouTubester.Worker;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the worker’s core services. Flags let tests opt out of hosted services / server.
    /// </summary>
    public static IServiceCollection AddWorkerCore(
        this IServiceCollection services,
        IConfiguration config,
        bool addHangfireServer = true)
    {
        services.Configure<WorkerOptions>(config.GetSection("Worker"));

        // DB
        services.AddDatabase(config);

        // External integrations
        services.AddBackgroundYoutubeServices(config);
        services.AddAiClient(config);

        // Repositories
        services.AddScoped<IChannelRepository, ChannelRepository>();
        services.AddScoped<IVideoRepository, VideoRepository>();
        services.AddScoped<IPlaylistRepository, PlaylistRepository>();
        services.AddScoped<IReplyRepository, ReplyRepository>();
        services.AddScoped<IUserTokenStore, UserTokenStore>();

        // Analytics
        services.AddScoped<IUserEventLogger, UserEventLogger>();

        // Credits
        services.AddScoped<ICreditsStore, CreditsStore>();
        services.AddScoped<ICreditsService, CreditsService>();

        // App services & jobs
        services.AddScoped<IAiVideoTemplatingService, AiVideoTemplatingService>();
        services.AddScoped<CommentScanJob>();
        services.AddScoped<AiTemplateJob>();
        services.AddScoped<AiTemplateFinalizeJob>();
        services.AddScoped<CreditPeriodMaintenanceJob>();

        var connectionString = config.GetConnectionString("YouTubesterDb")
                               ?? throw new InvalidOperationException("Missing connection string 'YouTubesterDb'.");


        services.AddSingleton<IDateTimeOffsetProvider, DateTimeOffsetProvider>();

        if (!addHangfireServer)
        {
            return services;
        }

        // Hangfire storage (no server yet)
        services.AddHangfire((_, cfg) =>
        {
            cfg.UsePostgreSqlStorage((options) =>
            {
                options.UseNpgsqlConnection(connectionString);
            });
            var jobsJsonPath = Path.Combine(AppContext.BaseDirectory, "recurring-jobs.json");
            cfg.UseRecurringJob(jobsJsonPath);
        });


        services.AddHangfireServer(o => o.Queues = ["scanning", "ai-templating", "default"]);

        return services;
    }
}