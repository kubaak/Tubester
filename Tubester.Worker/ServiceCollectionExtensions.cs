using Hangfire;
using Hangfire.PostgreSql;
using Hangfire.RecurringJobExtensions;
using Tubester.Abstractions.Analytics;
using Tubester.Abstractions.Auth;
using Tubester.Abstractions.Channels;
using Tubester.Abstractions.Credits;
using Tubester.Abstractions.Playlists;
using Tubester.Abstractions.Replies;
using Tubester.Abstractions.Videos;
using Tubester.Application;
using Tubester.Application.Credits;
using Tubester.Application.DomainEvents;
using Tubester.Application.Jobs;
using Tubester.Application.Videos;
using Tubester.Integration;
using Tubester.Persistence;
using Tubester.Persistence.Analytics;
using Tubester.Persistence.Channels;
using Tubester.Persistence.Credits;
using Tubester.Persistence.Playlists;
using Tubester.Persistence.Replies;
using Tubester.Persistence.Users;
using Tubester.Persistence.Videos;

namespace Tubester.Worker;

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

        // Domain event handlers
        services.AddDomainEventHandlers();

        // Credits
        services.AddScoped<ICreditsStore, CreditsStore>();
        services.AddScoped<ICreditsService, CreditsService>();

        // App services & jobs
        services.AddScoped<IAiVideoTemplatingService, AiVideoTemplatingService>();
        services.AddScoped<CommentScanJob>();
        services.AddScoped<AiTemplateJob>();
        services.AddScoped<AiTemplateFinalizeJob>();
        services.AddScoped<CreditPeriodMaintenanceJob>();

        var connectionString = config.GetConnectionString("TubesterDb")
                               ?? throw new InvalidOperationException("Missing connection string 'TubesterDb'.");


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