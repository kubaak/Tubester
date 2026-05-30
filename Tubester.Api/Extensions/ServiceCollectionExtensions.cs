using System.Net;
using System.Reflection;
using System.Security.Claims;
using Google;
using Google.Apis.YouTube.v3;
using Hangfire;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.OpenApi;
using Tubester.Abstractions;
using Tubester.Abstractions.Analytics;
using Tubester.Abstractions.Users;
using Tubester.Api.Auth;
using Tubester.Application.Jobs;
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
                policy.RequireClaim(TubesterClaimTypes.YouTubeWriteGranted, "true");
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

        // To use OAuth token in subsequent requests during the same signed-in session.
        options.SaveTokens = true;

        // No refresh token.
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
                var enrichmentResult = await TryEnrichYouTubeClaimsAsync(
                    context,
                    grantedClaimType: TubesterClaimTypes.YouTubeReadGranted,
                    alsoGrantReadAccess: false);

                if (string.IsNullOrWhiteSpace(enrichmentResult.AccessToken))
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

                var user = await userRepository.UpsertUserAsync(
                    userId,
                    email,
                    name,
                    picture,
                    now,
                    cancellationToken);

                if (user.IsNew && TryEnqueueInitialCommentScan(context, principal))
                {
                    user.MarkAsExisting();
                    await userRepository.UpdateUserAsync(user, cancellationToken);
                }

                await LogLoginAsync(context, userId);
            },
            OnRemoteFailure = _onRemoteFailure
        };
    }

    private static OAuthEvents CreateWriteOAuthEvents()
    {
        return new OAuthEvents
        {
            OnTicketReceived = async context =>
            {
                var enrichmentResult = await TryEnrichYouTubeClaimsAsync(
                    context,
                    grantedClaimType: TubesterClaimTypes.YouTubeWriteGranted,
                    alsoGrantReadAccess: true);

                if (string.IsNullOrWhiteSpace(enrichmentResult.AccessToken))
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

                var requestServices = context.HttpContext.RequestServices;
                var userEventLogger = requestServices.GetRequiredService<IUserEventLogger>();
                var cancellationToken = context.HttpContext.RequestAborted;

                await LogLoginAsync(context, userId);

                if (!enrichmentResult.YouTubePermissionGranted)
                {
                    return;
                }

                await userEventLogger.LogAsync(
                    userId,
                    UserEventType.WriteConsentGranted,
                    null,
                    null,
                    new { scheme = context.Scheme.Name },
                    cancellationToken);
            },
            OnRemoteFailure = _onRemoteFailure
        };
    }

    private static readonly Func<RemoteFailureContext, Task> _onRemoteFailure = context =>
    {
        var loggerFactory = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger("Tubester.Api.Authentication");

        logger.LogWarning(
            context.Failure,
            "External login failed. Scheme={Scheme}, Path={Path}",
            context.Scheme.Name,
            context.Request.Path);

        context.HandleResponse();

        foreach (var cookie in context.Request.Cookies.Keys)
        {
            if (cookie.StartsWith(".AspNetCore.Correlation.", StringComparison.OrdinalIgnoreCase) ||
                cookie.StartsWith(".AspNetCore.OpenIdConnect.Nonce.", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.Cookies.Delete(cookie, new CookieOptions
                {
                    Path = "/",
                    Secure = true,
                    SameSite = SameSiteMode.None
                });
            }
        }

        context.Response.Redirect("/login?error=external-login-failed");

        return Task.CompletedTask;
    };

    private sealed record YouTubeClaimEnrichmentResult(
        string? AccessToken,
        bool YouTubePermissionGranted);

    private static async Task<YouTubeClaimEnrichmentResult> TryEnrichYouTubeClaimsAsync(
        TicketReceivedContext context,
        string grantedClaimType,
        bool alsoGrantReadAccess)
    {
        var accessToken = context.Properties?.GetTokenValue("access_token");
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            var loggerFactory = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger("Tubester.Api.Authentication");

            logger.LogWarning(
                "Access token was not available during Google login; skipping channel enrichment");

            return new YouTubeClaimEnrichmentResult(null, false);
        }

        if (context.Principal?.Identity is not ClaimsIdentity claimsIdentity)
        {
            return new YouTubeClaimEnrichmentResult(accessToken, false);
        }

        try
        {
            var youTubeIntegration = context.HttpContext.RequestServices.GetRequiredService<IYouTubeIntegration>();
            var userChannel = await youTubeIntegration.GetCurrentChannelAsync(
                accessToken,
                context.HttpContext.RequestAborted);

            AddOrReplaceClaim(claimsIdentity, grantedClaimType, "true");

            if (alsoGrantReadAccess)
            {
                AddOrReplaceClaim(claimsIdentity, TubesterClaimTypes.YouTubeReadGranted, "true");
            }

            if (userChannel is null)
            {
                return new YouTubeClaimEnrichmentResult(accessToken, true);
            }

            AddOrReplaceClaim(claimsIdentity, TubesterClaimTypes.YouTubeChannelId, userChannel.Id);
            AddOrReplaceClaim(claimsIdentity, TubesterClaimTypes.YouTubeChannelTitle, userChannel.Title ?? string.Empty);
            AddOrReplaceClaim(claimsIdentity, TubesterClaimTypes.YouTubeChannelPicture, userChannel.Picture ?? string.Empty);
            AddOrReplaceClaim(claimsIdentity, TubesterClaimTypes.YouTubeUploadPlaylistId, userChannel.UploadPlaylistId ?? string.Empty);

            return new YouTubeClaimEnrichmentResult(accessToken, true);
        }
        catch (GoogleApiException exception) when (IsInsufficientPermissions(exception))
        {
            AddOrReplaceClaim(claimsIdentity, TubesterClaimTypes.YouTubeReadGranted, "false");
            return new YouTubeClaimEnrichmentResult(accessToken, false);
        }
    }

    private static bool IsInsufficientPermissions(GoogleApiException exception)
    {
        if (exception.HttpStatusCode != HttpStatusCode.Forbidden)
        {
            return false;
        }

        return exception.Error?.Errors?.Any(error =>
            string.Equals(
                error.Reason,
                "insufficientPermissions",
                StringComparison.OrdinalIgnoreCase)) == true;
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

    private static bool TryEnqueueInitialCommentScan(TicketReceivedContext context, ClaimsPrincipal principal)
    {
        var channelId = principal.FindFirstValue(TubesterClaimTypes.YouTubeChannelId);
        if (string.IsNullOrWhiteSpace(channelId))
        {
            return false;
        }

        var backgroundJobClient = context.HttpContext.RequestServices.GetRequiredService<IBackgroundJobClient>();

        backgroundJobClient.Enqueue<CommentScanJob>(
            job => job.Run(
                channelId,
                new CommentScanOptions(InitialRun: true),
                JobCancellationToken.Null));
        return true;
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