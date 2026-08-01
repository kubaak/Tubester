using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Tubester.Abstractions.Observability;

namespace Tubester.Observability;

/// <summary>
/// Provides methods for configuring and integrating Serilog logging into the application's hosting environment.
/// </summary>
public static class SerilogConfiguration
{
    /// <summary>
    /// Configures and adds a Serilog logger to the application, enriching logs with application-specific metadata
    /// such as application name and environment name. It also integrates with Seq logging if enabled
    /// in the configuration.
    /// </summary>
    /// <param name="builder">The <see cref="IHostApplicationBuilder"/> used to configure the application.</param>
    /// <param name="applicationName">The name of the application, which will be included in the log metadata.</param>
    /// <returns>The <see cref="IHostApplicationBuilder"/> instance with Serilog logging configured.</returns>
    public static IHostApplicationBuilder AddTubesterSerilog(
        this IHostApplicationBuilder builder,
        string applicationName)
    {
        var environment = builder.Environment;
        var configuration = builder.Configuration;

        builder.Services.Configure<ObservabilityOptions>(
            configuration.GetSection("Observability"));

        var observabilityOptions = configuration
            .GetSection("Observability")
            .Get<ObservabilityOptions>() ?? new ObservabilityOptions();

        var loggerConfig = new LoggerConfiguration()
            .ReadFrom.Configuration(configuration)
            .Enrich.With<LogLevelEnricher>()
            .Enrich.WithProperty("Application", applicationName)
            .Enrich.WithProperty("Environment", environment.EnvironmentName);

        if (observabilityOptions.Seq.Enabled &&
            !string.IsNullOrWhiteSpace(observabilityOptions.Seq.Url))
        {
            loggerConfig = loggerConfig.WriteTo.Seq(
                observabilityOptions.Seq.Url,
                apiKey: string.IsNullOrWhiteSpace(observabilityOptions.Seq.ApiKey)
                    ? null
                    : observabilityOptions.Seq.ApiKey);
        }

        Log.Logger = loggerConfig.CreateLogger();

        builder.Logging.ClearProviders();
        builder.Services.AddSerilog(Log.Logger, dispose: true);

        return builder;
    }
}
