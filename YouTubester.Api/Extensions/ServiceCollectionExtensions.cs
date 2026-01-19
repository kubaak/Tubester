using System.Reflection;
using System.Security.Claims;
using Google.Apis.YouTube.v3;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.OpenApi;
using YouTubester.Abstractions.Analytics;
using YouTubester.Abstractions.Auth;
using YouTubester.Abstractions.Users;
using YouTubester.Integration;

namespace YouTubester.Api.Extensions;

/// <summary>
/// 
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Auth setup: Cookie (session) + Google (login)
    /// </summary>
    /// <param name="services"></param>
    /// <param name="configuration"></param>
    /// <returns></returns>
    public static IServiceCollection AddCookieWithGoogle(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddAuthentication(options =>
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
                o.ClientId = configuration["GoogleAuth:ClientId"]!;
                o.ClientSecret = configuration["GoogleAuth:ClientSecret"]!;
                o.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                o.SaveTokens = true;
                o.AccessType = "offline";
                o.CallbackPath = "/api/auth/google/callback";
                o.CorrelationCookie.SameSite = SameSiteMode.None;
                o.CorrelationCookie.SecurePolicy = CookieSecurePolicy.Always;
                o.Scope.Add("openid");
                o.Scope.Add("profile");
                o.Scope.Add("email");
                o.Scope.Add(YouTubeService.Scope.YoutubeReadonly);
                o.Events = new OAuthEvents
                {
                    OnTicketReceived = async context =>
                    {
                        var accessToken = context.Properties?.GetTokenValue("access_token");
                        var refreshToken = context.Properties?.GetTokenValue("refresh_token");
                        var expiresAtRaw = context.Properties?.GetTokenValue("expires_at");
                        DateTimeOffset? expiresAt = null;
                        if (!string.IsNullOrWhiteSpace(expiresAtRaw) &&
                            DateTimeOffset.TryParse(expiresAtRaw, out var parsedExpiresAt))
                        {
                            expiresAt = parsedExpiresAt;
                        }

                        if (string.IsNullOrWhiteSpace(accessToken))
                        {
                            var loggerFactory = context.HttpContext.RequestServices
                                .GetRequiredService<ILoggerFactory>();
                            var logger = loggerFactory.CreateLogger("YouTubester.Api.Authentication");
                            logger.LogWarning(
                                "Access token was not available during Google login; skipping channel enrichment");
                            return;
                        }

                        var youTubeIntegration = context.HttpContext.RequestServices
                            .GetRequiredService<IYouTubeIntegration>();
                        var userChannel = await youTubeIntegration.GetCurrentChannelAsync(
                            accessToken, context.HttpContext.RequestAborted);

                        if (userChannel is null)
                        {
                            return;
                        }

                        var claimsIdentity = (ClaimsIdentity)context.Principal!.Identity!;
                        claimsIdentity.AddClaim(new Claim("yt_channel_id", userChannel.Id));
                        claimsIdentity.AddClaim(new Claim("yt_channel_title", userChannel.Title ?? string.Empty));
                        claimsIdentity.AddClaim(new Claim("yt_channel_picture", userChannel.Picture ?? string.Empty));

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
                        var userTokenStore = requestServices.GetRequiredService<IUserTokenStore>();
                        var userEventLogger = requestServices.GetRequiredService<IUserEventLogger>();
                        var cancellationToken = context.HttpContext.RequestAborted;
                        var now = DateTimeOffset.UtcNow;
                        await userRepository.UpsertUserAsync(userId, email, name, picture, now, cancellationToken);
                        await userTokenStore.UpsertAsync(
                            userId,
                            accessToken,
                            refreshToken,
                            expiresAt,
                            cancellationToken);

                        await userEventLogger.LogAsync(
                            userId,
                            UserEventType.Login,
                            null,
                            null,
                            new
                            {
                                scheme = context.Scheme.Name,
                                hasRefreshToken = !string.IsNullOrWhiteSpace(refreshToken)
                            },
                            cancellationToken);
                    }
                };
            })
            .AddGoogle("GoogleWrite", o =>
            {
                o.ClientId = configuration["GoogleAuth:ClientId"]!;
                o.ClientSecret = configuration["GoogleAuth:ClientSecret"]!;
                o.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                o.SaveTokens = true;
                o.AccessType = "online";
                o.CallbackPath = "/api/auth/google/write/callback";
                o.CorrelationCookie.SameSite = SameSiteMode.None;
                o.CorrelationCookie.SecurePolicy = CookieSecurePolicy.Always;
                o.Scope.Add("openid");
                o.Scope.Add("profile");
                o.Scope.Add("email");
                o.Scope.Add(YouTubeService.Scope.YoutubeForceSsl);

                o.Events = CreateOAuthEvents();
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

        OAuthEvents CreateOAuthEvents()
        {
            return new OAuthEvents
            {
                OnTicketReceived = async context =>
                {
                    var accessToken = context.Properties?.GetTokenValue("access_token");
                    if (string.IsNullOrWhiteSpace(accessToken))
                    {
                        var loggerFactory = context.HttpContext.RequestServices
                            .GetRequiredService<ILoggerFactory>();
                        var logger = loggerFactory.CreateLogger("YouTubester.Api.Authentication");
                        logger.LogWarning(
                            "Access token was not available during Google login; skipping channel enrichment.");
                        return;
                    }

                    var youTubeIntegration = context.HttpContext.RequestServices
                        .GetRequiredService<IYouTubeIntegration>();
                    var userChannel = await youTubeIntegration.GetCurrentChannelAsync(
                        accessToken,
                        context.HttpContext.RequestAborted);

                    if (userChannel is null)
                    {
                        return;
                    }

                    var claimsIdentity = (ClaimsIdentity)context.Principal!.Identity!;
                    claimsIdentity.AddClaim(new Claim("yt_channel_id", userChannel.Id));
                    claimsIdentity.AddClaim(new Claim("yt_channel_title", userChannel.Title ?? string.Empty));
                    claimsIdentity.AddClaim(new Claim("yt_channel_picture", userChannel.Picture ?? string.Empty));

                    claimsIdentity.AddClaim(new Claim("yt_write_granted", "true"));

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

                    var refreshToken = context.Properties?.GetTokenValue("refresh_token");

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
                            scheme = context.Scheme.Name,
                            hasRefreshToken = !string.IsNullOrWhiteSpace(refreshToken)
                        },
                        cancellationToken);

                    await userEventLogger.LogAsync(
                        userId,
                        UserEventType.WriteConsentGranted,
                        null,
                        null,
                        new
                        {
                            scheme = context.Scheme.Name
                        },
                        cancellationToken);
                }
            };
        }
    }

    /// <summary>
    /// Adds swagger services
    /// </summary>
    /// <param name="services"></param>
    /// <returns></returns>
    public static IServiceCollection AddSwagger(this IServiceCollection services)
    {
        services.AddSwaggerGen(options =>
        {
            options.SupportNonNullableReferenceTypes();

            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "YouTubester API",
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
}