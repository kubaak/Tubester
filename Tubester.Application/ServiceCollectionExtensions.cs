using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tubester.Abstractions.ApplicationConfiguration;
using Tubester.Application.Options;
using Tubester.Integration;

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

    public static IServiceCollection AddReplyListingOptions(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<ReplyListingOptions>(configuration.GetSection("ReplyListing"));
        services.AddOptions<ReplyListingOptions>()
            .ValidateDataAnnotations()
            .ValidateOnStart();
        return services;
    }

    public static IServiceCollection AddPlaylistSuggestionOptions(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<PlaylistSuggestionOptions>(configuration.GetSection("AI"));
        services.AddOptions<PlaylistSuggestionOptions>()
            .ValidateDataAnnotations()
            .ValidateOnStart();
        return services;
    }
    
    public static IServiceCollection AddApplicationConfigurationServices(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddScoped<IApplicationConfigurationService, ApplicationConfigurationService>();
        services.AddScoped<IFeatureFlagService, FeatureFlagService>();
        services.AddScoped<IAiRuntimeOptionsService, AiRuntimeOptionsService>();
        services.AddScoped<IAiClientFactory, AiClientFactory>();

        return services;
    }
}
