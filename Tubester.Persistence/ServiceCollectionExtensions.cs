using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tubester.Abstractions.DomainEvents;
using Tubester.Persistence.DomainEvents;

namespace Tubester.Persistence;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("TubesterDb");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Missing connection string 'ConnectionStrings:TubesterDb'.");
        }

        services.AddScoped<DomainEventInterceptor>();
        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
        //todo AddDbContextFactory
        services.AddDbContext<TubesterDb>((serviceProvider, options) =>
        {
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.UseVector();
            });
            options.AddInterceptors(serviceProvider.GetRequiredService<DomainEventInterceptor>());
        });
        return services;
    }

    public static IServiceCollection AddHangFireStorage(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("TubesterDb");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Missing connection string 'ConnectionStrings:TubesterDb'.");
        }

        services.AddHangfire(configurationExpression =>
            configurationExpression.UsePostgreSqlStorage(options =>
            {
                options.UseNpgsqlConnection(connectionString);
            }));

        return services;
    }
}