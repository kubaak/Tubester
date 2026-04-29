using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Tubester.Abstractions.Auth;
using Tubester.Integration.Configuration;

namespace Tubester.Integration;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddOnlineYoutubeServices(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<GoogleAuthOptions>(configuration.GetSection("GoogleAuth"));
        services.AddScoped<IYouTubeIntegration, YouTubeIntegration>();

        return services;
    }

    public static IServiceCollection AddBackgroundYoutubeServices(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<GoogleAuthOptions>(configuration.GetSection("GoogleAuth"));
        services.Configure<YouTubeApiOptions>(configuration.GetSection("YouTubeApi"));
        services.AddHttpClient<IGoogleTokenRefresher, GoogleTokenRefresher>();
        services.AddScoped<IBackgroundYoutubeIntegration, BackgroundYoutubeIntegration>();

        return services;
    }

    public static IServiceCollection AddAiClient(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AiOptions>(configuration.GetSection("AI"));
        services.AddHttpClient<IAiClient, AiClient>((sp, http) =>
            {
                var ai = sp.GetRequiredService<IOptions<AiOptions>>().Value;
                http.BaseAddress = new Uri(ai.Endpoint);
            })
            .AddStandardResilienceHandler(options =>
                {
                    options.AttemptTimeout = new HttpTimeoutStrategyOptions { Timeout = TimeSpan.FromSeconds(90) };
                    options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(180);
                    options.TotalRequestTimeout = new HttpTimeoutStrategyOptions { Timeout = TimeSpan.FromMinutes(5) };
                }
            );

        return services;
    }
}
