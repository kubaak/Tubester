using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tubester.Abstractions.Observability;
using Tubester.Persistence;

namespace Tubester.Application.Observability;

/// <summary>
/// Background service that periodically refreshes business metric counts.
/// Runs on a configurable interval to avoid expensive DB queries on every scrape.
/// </summary>
public sealed class BusinessMetricsRefreshService(
    IServiceScopeFactory scopeFactory,
    BusinessMetricsGauges gauges,
    IOptions<ObservabilityOptions> options,
    ILogger<BusinessMetricsRefreshService> logger)
    : BackgroundService
{
    private readonly ObservabilityOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled || !_options.BusinessMetrics.Enabled)
        {
            return;
        }

        var intervalSeconds = _options.BusinessMetrics.RefreshIntervalSeconds;
        var interval = TimeSpan.FromSeconds(intervalSeconds);

        logger.LogInformation(
            "Business metrics refresh service started. Refresh interval: {IntervalSeconds}s",
            intervalSeconds);

        try
        {
            await RefreshMetricsAsync(stoppingToken);

            using var timer = new PeriodicTimer(interval);

            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await RefreshMetricsAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("Business metrics refresh service stopped");
        }
    }

    private async Task RefreshMetricsAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<TubesterDb>();

            var users = await dbContext.Users.LongCountAsync(cancellationToken);
            var channels = await dbContext.Set<Domain.Channel>().LongCountAsync(cancellationToken);
            var videos = await dbContext.Videos.LongCountAsync(cancellationToken);
            var replies = await dbContext.Replies.LongCountAsync(cancellationToken);

            gauges.UpdateCounts(users, channels, videos, replies);

            logger.LogDebug(
                "Business metrics refreshed. Users={Users}, Channels={Channels}, Videos={Videos}, Replies={Replies}",
                users, channels, videos, replies);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal shutdown. Do not log as warning.
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to refresh business metrics. Will retry on next interval");
        }
    }
}
