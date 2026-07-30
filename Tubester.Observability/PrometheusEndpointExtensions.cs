using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Tubester.Abstractions.Observability;

namespace Tubester.Observability;

/// <summary>
/// Extension methods for mapping Prometheus metrics endpoints.
/// </summary>
public static class PrometheusEndpointExtensions
{
    private const string MetricsEndpoint = "/metrics";

    /// <summary>
    /// Maps the Prometheus metrics scraping endpoint at <c>/metrics</c>.
    /// Only enabled when <c>Observability:Enabled</c> and <c>Observability:Prometheus:Enabled</c> are true.
    /// </summary>
    /// <param name="app">The web application.</param>
    /// <returns>The configured web application.</returns>
    public static WebApplication MapTubesterMetricsEndpoint(this WebApplication app)
    {
        var observabilityOptions = app.Configuration
            .GetSection("Observability")
            .Get<ObservabilityOptions>() ?? new ObservabilityOptions();

        if (!observabilityOptions.Enabled || !observabilityOptions.Prometheus.Enabled)
        {
            return app;
        }

        app.MapPrometheusScrapingEndpoint(MetricsEndpoint);

        app.Logger.LogDebug(
            "Prometheus metrics endpoint mapped at {Endpoint}",
            MetricsEndpoint);

        return app;
    }
}
