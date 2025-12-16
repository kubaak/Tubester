using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace YouTubester.Persistence;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("YouTubesterDb");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Missing connection string 'ConnectionStrings:YouTubesterDb'.");
        }

        services.AddDbContext<YouTubesterDb>(options => options.UseNpgsql(connectionString));
        return services;
    }

    public static IServiceCollection AddHangFireStorage(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("YouTubesterDb");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Missing connection string 'ConnectionStrings:YouTubesterDb'.");
        }

        services.AddHangfire(configurationExpression =>
            configurationExpression.UsePostgreSqlStorage(options =>
            {
                options.UseNpgsqlConnection(connectionString);
            }));

        return services;
    }
}