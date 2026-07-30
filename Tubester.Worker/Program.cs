using Microsoft.AspNetCore.Builder;
using Tubester.Observability;
using Tubester.Worker;

var builder = WebApplication.CreateBuilder(args);

// Add Serilog for structured logging
builder.AddTubesterSerilog("Tubester.Worker");

builder.Services.AddWorkerCore(builder.Configuration);

// Add health checks
builder.Services.AddTubesterPostgresHealthChecks(
    builder.Configuration,
    serviceDisplayName: "Worker");

// Add OpenTelemetry metrics
builder.AddTubesterOpenTelemetryMetrics("Tubester.Worker", false);

var app = builder.Build();

// Map observability endpoints
app.MapTubesterMetricsEndpoint();
app.MapTubesterHealthEndpoints();

app.Logger.LogInformation("Tubester Worker started with health and metrics endpoints enabled");

await app.RunAsync();

namespace Tubester.Worker
{
    /// <summary>
    /// Make Program class accessible for integration testing.
    /// </summary>
    public class Program;
}