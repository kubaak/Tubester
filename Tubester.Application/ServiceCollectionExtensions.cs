using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tubester.Application.Options;

namespace Tubester.Application;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddVideoListingOptions(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<VideoListingOptions>(configuration.GetSection("VideoListing"));
        services.AddOptions<VideoListingOptions>()
            .ValidateDataAnnotations()
            .ValidateOnStart();
        return services;
    }
}