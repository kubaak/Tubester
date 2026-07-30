using Microsoft.Extensions.DependencyInjection;
using Tubester.Abstractions.Observability;

namespace Tubester.Observability;

/// <summary>
/// Extension methods for registering Tubester metrics services.
/// </summary>
public static class MetricsCoreServiceCollectionExtensions
{
    public static IServiceCollection AddTubesterMetricsCore(
        this IServiceCollection services)
    {
        services.AddMetrics();
        services.AddSingleton<ITubesterMetrics, TubesterMetrics>();

        return services;
    }
}