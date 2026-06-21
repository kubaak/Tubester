using Hangfire;
using Hangfire.PostgreSql;
using Hangfire.RecurringJobExtensions;
using Tubester.Abstractions;
using Tubester.Abstractions.Analytics;
using Tubester.Abstractions.Channels;
using Tubester.Abstractions.Credits;
using Tubester.Abstractions.Playlists;
using Tubester.Abstractions.Replies;
using Tubester.Abstractions.Transactions;
using Tubester.Abstractions.Videos;
using Tubester.Application;
using Tubester.Application.Channels;
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
using Tubester.Persistence.Transactions;
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
        services.AddPlaylistSuggestionOptions(config);
        services.AddApplicationConfigurationServices();

        // Repositories
        services.AddScoped<IChannelRepository, ChannelRepository>();
        services.AddScoped<IChannelSettingsRepository, ChannelSettingsRepository>();
        services.AddScoped<IVideoRepository, VideoRepository>();
        services.AddScoped<IPlaylistRepository, PlaylistRepository>();
        services.AddScoped<IReplyRepository, ReplyRepository>();

        // Analytics
        services.AddScoped<IUserEventLogger, UserEventLogger>();

        // Domain event handlers
        services.AddDomainEventHandlers();

        // Credits
        services.AddScoped<ICreditsStore, CreditsStore>();
        services.AddScoped<ICreditsService, CreditsService>();

        // App services & jobs
        services.AddScoped<IChannelSettingsService, ChannelSettingsService>();
        services.AddScoped<IAiVideoImprovingService, AiVideoImprovingService>();
        services.AddScoped<CommentScanJob>();
        services.AddScoped<AiTemplateJob>();
        services.AddScoped<AiPlaylistSuggestionJob>();
        services.AddScoped<AiTemplateFinalizeJob>();
        services.AddScoped<CreditPeriodMaintenanceJob>();
        services.AddScoped<ReplyEmbeddingBackfillJob>();

        var connectionString = config.GetConnectionString("TubesterDb")
                               ?? throw new InvalidOperationException("Missing connection string 'TubesterDb'.");


        services.AddSingleton<IDateTimeOffsetProvider, DateTimeOffsetProvider>();
        services.AddScoped<IApplicationTransactionRunner, EfApplicationTransactionRunner>();

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


        services.AddHangfireServer(o => o.Queues = ["scanning", "ai-templating", "ai-playlist-suggestion", "embeddings", "default"]);

        return services;
    }
}