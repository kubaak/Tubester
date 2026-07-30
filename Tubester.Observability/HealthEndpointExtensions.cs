using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Tubester.Observability;

/// <summary>
/// Extension methods for mapping health check endpoints.
/// </summary>
public static class HealthEndpointExtensions
{
    private const string HealthLiveEndpoint = "/health/live";
    private const string HealthReadyEndpoint = "/health/ready";

    /// <summary>
    /// Maps Tubester health check endpoints:
    /// - <c>/health/live</c> for liveness probes (self health check only)
    /// - <c>/health/ready</c> for readiness probes (dependency checks)
    /// </summary>
    /// <param name="app">The web application.</param>
    /// <returns>The configured web application.</returns>
    public static WebApplication MapTubesterHealthEndpoints(this WebApplication app)
    {
        // Liveness probe - lightweight, returns OK if the process is running
        app.MapHealthChecks(HealthLiveEndpoint, new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("live")
        });

        app.Logger.LogDebug(
            "Health liveness endpoint mapped at {Endpoint}",
            HealthLiveEndpoint);

        // Readiness probe - checks dependencies like database
        app.MapHealthChecks(HealthReadyEndpoint, new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("ready")
        });

        app.Logger.LogDebug(
            "Health readiness endpoint mapped at {Endpoint}",
            HealthReadyEndpoint);

        return app;
    }
}
