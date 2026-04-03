using System.Reflection;
using System.Security.Claims;
using Google.Apis.YouTube.v3;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.OpenApi;
using Tubester.Abstractions.Analytics;
using Tubester.Abstractions.Users;
using Tubester.Application;
using Tubester.Integration;

namespace Tubester.Api.Extensions;

/// <summary>
/// Service registration extensions.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Auth setup: Cookie (session) + Google (login).
    /// </summary>
    public static IServiceCollection AddCookieWithGoogle(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddAuthentication(options =>
            {
                options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            })
            .AddCookie(o =>
            {
                o.Cookie.HttpOnly = true;
                o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                o.Cookie.SameSite = SameSiteMode.Lax;
                o.SlidingExpiration = true;
                o.ExpireTimeSpan = TimeSpan.FromHours(12);
                o.LoginPath = "/api/auth/login/google";
                o.LogoutPath = "/api/auth/logout";

                o.Events = new CookieAuthenticationEvents
                {
                    OnRedirectToLogin = ctx =>
                    {
                        if (ctx.Request.Path.StartsWithSegments("/api"))
                        {
                            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                            return Task.CompletedTask;
                        }

                        var returnUrl = ctx.Request.Path + ctx.Request.QueryString;
                        var redirectUri = $"/api/auth/login/google?returnUrl={Uri.EscapeDataString(returnUrl)}";
                        ctx.Response.Redirect(redirectUri);
                        return Task.CompletedTask;
                    },
                    OnRedirectToAccessDenied = ctx =>
                    {
                        ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                        return Task.CompletedTask;
                    }
                };
            })
            .AddGoogle("GoogleRead", o =>
            {
                ConfigureGoogleOptions(
                    o,
                    configuration,
                    callbackPath: "/api/auth/google/callback",
                    youtubeScope: YouTubeService.Scope.YoutubeReadonly,
                    events: CreateReadOAuthEvents());
            })
            .AddGoogle("GoogleWrite", o =>
            {
                ConfigureGoogleOptions(
                    o,
                    configuration,
                    callbackPath: "/api/auth/google/write/callback",
                    youtubeScope: YouTubeService.Scope.YoutubeForceSsl,
                    events: CreateWriteOAuthEvents());
            });

        services.AddAuthorization(options =>
        {
            options.AddPolicy("RequiresYouTubeWrite", policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireClaim("yt_write_granted", "true");
            });
        });

        return services;
    }

    /// <summary>
    /// Adds swagger services.
    /// </summary>
    public static IServiceCollection AddSwagger(this IServiceCollection services)
    {
        services.AddSwaggerGen(options =>
        {
            options.SupportNonNullableReferenceTypes();

            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Tubester API",
                Version = "v1",
                Description =
                    "To access protected endpoints, first log in:\n\n" +
                    "[🔐 read only Login with Google](/api/auth/login/google?returnUrl=/swagger/index.html)\n\n" +
                    "[🔐 write Login with Google](/api/auth/login/google/write?returnUrl=/swagger/index.html)"
            });

            var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
            var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
            if (File.Exists(xmlPath))
            {
                options.IncludeXmlComments(xmlPath);
            }

            options.OperationFilter<Swagger.RequiresYouTubeWriteOperationFilter>();
        });

        return services;
    }

    private static void ConfigureGoogleOptions(
        GoogleOptions options,
        IConfiguration configuration,
        string callbackPath,
        string youtubeScope,
        OAuthEvents events)
    {
        options.ClientId = configuration["GoogleAuth:ClientId"]!;
        options.ClientSecret = configuration["GoogleAuth:ClientSecret"]!;
        options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        //to use OAuth token in the subsequent requests in the same signed-in session
        options.SaveTokens = true;
        //no refresh token
        options.AccessType = "online";
        options.CallbackPath = callbackPath;
        options.CorrelationCookie.SameSite = SameSiteMode.None;
        options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.Always;

        options.Scope.Add("openid");
        options.Scope.Add("profile");
        options.Scope.Add("email");
        options.Scope.Add(youtubeScope);

        options.Events = events;
    }

    private static OAuthEvents CreateReadOAuthEvents()
    {
        return new OAuthEvents
        {
            OnTicketReceived = async context =>
            {
                var accessToken = await TryEnrichYouTubeClaimsAsync(context);
                if (string.IsNullOrWhiteSpace(accessToken))
                {
                    return;
                }

                var principal = context.Principal;
                if (principal is null)
                {
                    return;
                }

                var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrWhiteSpace(userId))
                {
                    return;
                }

                var email = principal.FindFirstValue(ClaimTypes.Email);
                var name = principal.Identity?.Name;
                var picture = principal.FindFirst("picture")?.Value;

                var requestServices = context.HttpContext.RequestServices;
                var userRepository = requestServices.GetRequiredService<IUserRepository>();
                var dateTimeOffsetProvider = requestServices.GetRequiredService<IDateTimeOffsetProvider>();
                var cancellationToken = context.HttpContext.RequestAborted;
                var now = dateTimeOffsetProvider.GetUtcNowDateTimeOffset();

                await userRepository.UpsertUserAsync(
                    userId,
                    email,
                    name,
                    picture,
                    now,
                    cancellationToken);

                await LogLoginAsync(context, userId);
            }
        };
    }

    private static OAuthEvents CreateWriteOAuthEvents()
    {
        return new OAuthEvents
        {
            OnTicketReceived = async context =>
            {
                var accessToken = await TryEnrichYouTubeClaimsAsync(context);
                if (string.IsNullOrWhiteSpace(accessToken))
                {
                    return;
                }

                var principal = context.Principal;
                if (principal is null)
                {
                    return;
                }

                var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrWhiteSpace(userId))
                {
                    return;
                }

                var claimsIdentity = (ClaimsIdentity)principal.Identity!;
                claimsIdentity.AddClaim(new Claim("yt_write_granted", "true"));

                var requestServices = context.HttpContext.RequestServices;
                var userEventLogger = requestServices.GetRequiredService<IUserEventLogger>();
                var cancellationToken = context.HttpContext.RequestAborted;

                await LogLoginAsync(context, userId);

                await userEventLogger.LogAsync(
                    userId,
                    UserEventType.WriteConsentGranted,
                    null,
                    null,
                    new { scheme = context.Scheme.Name },
                    cancellationToken);
            }
        };
    }

    private static async Task<string?> TryEnrichYouTubeClaimsAsync(TicketReceivedContext context)
    {
        var accessToken = context.Properties?.GetTokenValue("access_token");
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            var loggerFactory = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger("Tubester.Api.Authentication");
            logger.LogWarning(
                "Access token was not available during Google login; skipping channel enrichment");
            return null;
        }

        var youTubeIntegration = context.HttpContext.RequestServices.GetRequiredService<IYouTubeIntegration>();
        var userChannel = await youTubeIntegration.GetCurrentChannelAsync(
            accessToken,
            context.HttpContext.RequestAborted);

        if (userChannel is null || context.Principal?.Identity is not ClaimsIdentity claimsIdentity)
        {
            return accessToken;
        }

        AddOrReplaceClaim(claimsIdentity, "yt_channel_id", userChannel.Id);
        AddOrReplaceClaim(claimsIdentity, "yt_channel_title", userChannel.Title ?? string.Empty);
        AddOrReplaceClaim(claimsIdentity, "yt_channel_picture", userChannel.Picture ?? string.Empty);

        return accessToken;
    }

    private static async Task LogLoginAsync(TicketReceivedContext context, string userId)
    {
        var requestServices = context.HttpContext.RequestServices;
        var userEventLogger = requestServices.GetRequiredService<IUserEventLogger>();
        var cancellationToken = context.HttpContext.RequestAborted;

        await userEventLogger.LogAsync(
            userId,
            UserEventType.Login,
            null,
            null,
            new
            {
                scheme = context.Scheme.Name
            },
            cancellationToken);
    }

    private static void AddOrReplaceClaim(ClaimsIdentity identity, string claimType, string value)
    {
        var existingClaims = identity.FindAll(claimType).ToList();
        foreach (var existingClaim in existingClaims)
        {
            identity.RemoveClaim(existingClaim);
        }

        identity.AddClaim(new Claim(claimType, value));
    }
}