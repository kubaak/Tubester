using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tubester.Abstractions.Observability;

namespace Tubester.Observability;

/// <summary>
/// Extension methods for configuring observability options.
/// </summary>
public static class ObservabilityOptionsExtensions
{
    /// <summary>
    /// Binds the <see cref="ObservabilityOptions"/> from the "Observability" configuration section.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddObservabilityOptions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<ObservabilityOptions>(
            configuration.GetSection("Observability"));

        return services;
    }
}
