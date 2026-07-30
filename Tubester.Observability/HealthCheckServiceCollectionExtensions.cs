using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Tubester.Observability;

public static class HealthCheckServiceCollectionExtensions
{
    public static IServiceCollection AddTubesterPostgresHealthChecks(
        this IServiceCollection services,
        IConfiguration configuration,
        string serviceDisplayName,
        string connectionStringName = "TubesterDb")
    {
        var connectionString = configuration.GetConnectionString(connectionStringName)
                               ?? throw new InvalidOperationException(
                                   $"Missing connection string: {connectionStringName}");

        services.AddHealthChecks()
            .AddCheck(
                "self",
                () => HealthCheckResult.Healthy($"{serviceDisplayName} is running"),
                tags: ["live"])
            .AddNpgSql(
                connectionString,
                name: "postgresql",
                tags: ["ready"]);

        return services;
    }
}