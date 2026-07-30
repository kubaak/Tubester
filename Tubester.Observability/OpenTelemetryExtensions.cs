using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using Tubester.Abstractions.Observability;

namespace Tubester.Observability;

public static class OpenTelemetryExtensions
{
    public static IHostApplicationBuilder AddTubesterOpenTelemetryMetrics(
        this IHostApplicationBuilder builder,
        string serviceName,
        bool includeAspNetCoreInstrumentation)
    {
        var observabilityOptions = builder.Configuration
            .GetSection("Observability")
            .Get<ObservabilityOptions>() ?? new ObservabilityOptions();

        // Register custom metrics even if Prometheus export is disabled,
        // so services can safely inject ITubesterMetrics.
        builder.Services.AddTubesterMetricsCore();

        if (!observabilityOptions.Enabled || !observabilityOptions.Prometheus.Enabled)
        {
            return builder;
        }

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource =>
            {
                resource.AddService(serviceName);
                resource.AddAttributes([
                    new KeyValuePair<string, object>(
                        "deployment.environment",
                        builder.Environment.EnvironmentName)
                ]);
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddMeter(MetricNames.MeterName)
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddProcessInstrumentation()
                    .AddPrometheusExporter();

                if (includeAspNetCoreInstrumentation)
                {
                    metrics.AddAspNetCoreInstrumentation();
                }
            });

        return builder;
    }
}