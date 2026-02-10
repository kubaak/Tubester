## Current state

### 1) Authentication/authorization setup (API)

Right now I configure auth like this:

```csharp
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
                        var logger = loggerFactory.CreateLogger("Tubester.Api.Authentication");
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
                    var cancellationToken = context.HttpContext.RequestAborted;
                    var now = DateTimeOffset.UtcNow;
                    await userRepository.UpsertUserAsync(userId, email, name, picture, now, cancellationToken);
                    await userTokenStore.UpsertAsync(
                        userId,
                        accessToken,
                        refreshToken,
                        expiresAt,
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
                    var logger = loggerFactory.CreateLogger("Tubester.Api.Authentication");
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
            }
        };
    }
}
Key points:

.AddGoogle("GoogleRead") is where YouTube read tokens (including refresh) are currently captured and stored via IUserTokenStore.

There is no custom controller callback; everything happens inside the Google handler’s built-in callback and OnTicketReceived.

2) Token persistence abstraction
csharp
Copy code
public sealed class UserTokenData
{
    public string UserId { get; init; } = default!;
    public string? AccessToken { get; init; }
    public string? RefreshToken { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
}

public interface IUserTokenStore
{
    Task<UserTokenData?> GetAsync(string userId, CancellationToken cancellationToken);

    Task UpsertAsync(
        string userId,
        string? accessToken,
        string? refreshToken,
        DateTimeOffset? expiresAt,
        CancellationToken cancellationToken);
}
New goal
Add a dedicated “Connect YouTube” flow that uses:

GoogleAuthorizationCodeFlow

a custom IDataStore backed by IUserTokenStore / userTokens table

explicit endpoints like:

GET /api/auth/youtube/connect – starts the OAuth dance

GET /api/auth/youtube/connect/callback – Google redirects here with code

The idea:

Keep .AddCookieWithGoogle for login and identity (who the user is) – minimal changes there.

Have the YouTube connection and token lifecycle managed by Google’s standard flow classes instead of relying on the opaque OnTicketReceived + GetTokenValue(...).

Background jobs (worker) will later use GoogleAuthorizationCodeFlow + IDataStore + UserCredential to build YouTubeService with automatic refresh.

For now, focus only on adding the dedicated connect flow; we can adjust the worker later.

Tasks
1) Introduce UserTokenDataStore implementing Google.Apis.Util.Store.IDataStore
Create a new class, e.g. in Tubester.Integration.Auth or similar:

UserTokenDataStore : IDataStore

It depends on IUserTokenStore.

Responsibilities:

StoreAsync<T>:

If typeof(T) == typeof(TokenResponse):

Cast value to TokenResponse.

Compute ExpiresAt using IssuedUtc + ExpiresInSeconds if available.

Call IUserTokenStore.UpsertAsync(userId, accessToken, refreshToken, expiresAt, CancellationToken.None).

For other types, ignore or implement no-op.

GetAsync<T>:

If typeof(T) == typeof(TokenResponse):

Load UserTokenData via IUserTokenStore.GetAsync(userId, CancellationToken.None).

If null, return default.

Otherwise construct a TokenResponse:

AccessToken = data.AccessToken

RefreshToken = data.RefreshToken

You can set IssuedUtc to DateTimeOffset.UtcNow minus something, or leave it null; the library will still refresh when needed as long as refresh token exists.

DeleteAsync<T> and ClearAsync() can be no-ops for now (or TODO); I’ll likely only call them when/if I add a “disconnect Google” feature.

2) Register GoogleAuthorizationCodeFlow + UserTokenDataStore in DI
Add a helper extension, e.g. AddYoutubeAuthFlow:

services.Configure<YouTubeAuthOptions>(config.GetSection("GoogleAuth")) or similar.

services.AddScoped<IDataStore, UserTokenDataStore>();

services.AddScoped<GoogleAuthorizationCodeFlow>(sp => { ... }):

Inside the factory:

Resolve IOptions<YouTubeAuthOptions> (or GoogleAuth section I already use for .AddGoogle).

Resolve IDataStore (our UserTokenDataStore).

Return a GoogleAuthorizationCodeFlow with:

csharp
Copy code
new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
{
    ClientSecrets = new ClientSecrets
    {
        ClientId = options.ClientId,
        ClientSecret = options.ClientSecret
    },
    Scopes = new[]
    {
        YouTubeService.Scope.YoutubeReadonly,
        // (optionally Youtube and YoutubeForceSsl too if we want a single flow)
    },
    DataStore = dataStore
});
Wire AddYoutubeAuthFlow(configuration) from my API startup alongside AddCookieWithGoogle.

3) Add a dedicated AuthController with “Connect YouTube” endpoints
Create a controller in the API, e.g. AuthController under /api/auth route.

Add two actions:

GET /api/auth/youtube/connect

Requires an authenticated user via cookie (e.g. [Authorize]).

Resolve GoogleAuthorizationCodeFlow from DI.

Build an authorization URL:

csharp
Copy code
var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
var redirectUri = "<absolute URL to /api/auth/youtube/connect/callback>";
var state = "<some state or returnUrl if you want>";

var authUrl = flow.CreateAuthorizationCodeRequest(redirectUri);
authUrl.State = state;
// Optionally set AccessType = "offline" if needed (depends on flow/API)
var url = authUrl.Build().AbsoluteUri;
Return a redirect to url.

GET /api/auth/youtube/connect/callback

Also under [Authorize].

Accept code and state from query.

Resolve GoogleAuthorizationCodeFlow from DI.

Get userId from the principal (same logic as everywhere else).

Call flow.ExchangeCodeForTokenAsync(userId, code, redirectUri, cancellationToken):

This both returns a TokenResponse and calls IDataStore.StoreAsync → IUserTokenStore.UpsertAsync.

Optionally fetch the user’s YouTube channel here:

Use new UserCredential(flow, userId, tokenResponse) + YouTubeService to call YouTube and store/update channel info and claims if needed.

Redirect back to some front-end page or return an OK (e.g. “YouTube connected”).

4) Adjust .AddCookieWithGoogle to stop manually persisting YouTube tokens (optional, but preferred)
Once the dedicated connect flow is in place, we can simplify the OnTicketReceived for "GoogleRead":

Keep:

Enriching the identity with channel info if we still want that.

Saving User entity (email, name, picture) via IUserRepository.UpsertUserAsync.

Remove or minimize:

Direct calls to IUserTokenStore.UpsertAsync (because token persistence is now owned by the GoogleAuthorizationCodeFlow + UserTokenDataStore in the /youtube/connect/callback endpoint).

The idea:

.AddGoogle("GoogleRead") is for login + basic identity.

GoogleAuthorizationCodeFlow + UserTokenDataStore + ConnectYouTube endpoints are for long-lived YouTube API tokens.

5) Don’t touch the worker yet (but keep future changes in mind)
For now, goal is only to:

Add the dedicated connect endpoints + flow.

Make sure tokens end up in userTokens via UserTokenDataStore.

Later, I’ll refactor BackgroundYoutubeIntegration to:

Resolve GoogleAuthorizationCodeFlow from DI.

Build a UserCredential for userId from stored tokens.

Use UserCredential as HttpClientInitializer for YouTubeService so token refresh is automatic (no manual GoogleTokenRefresher).

Style / expectations
Keep naming and namespaces aligned with the existing code (Tubester.Api, Tubester.Integration, Tubester.Abstractions.Auth, etc.).

Use Google’s official client APIs where appropriate:

GoogleAuthorizationCodeFlow

TokenResponse

UserCredential

IDataStore

The new dedicated flow should not break existing login behavior via .AddCookieWithGoogle. Think of it as an extra step: “Log in with Google” (identity) vs “Connect YouTube” (YouTube API tokens).

Now:

Generate the concrete code for:

UserTokenDataStore implementing IDataStore using IUserTokenStore.

DI registration method for the flow (e.g. AddYoutubeAuthFlow).

AuthController (or YouTubeAuthController) with:

GET /api/auth/youtube/connect

GET /api/auth/youtube/connect/callback

Optionally show the minimal adjustments needed in AddCookieWithGoogle (just outline changes, no need to fully rewrite everything).

Ensure the code is coherent and compile-ready, even if I still need to plug in exact URLs/state values.

makefile
Copy code
::contentReference[oaicite:0]{index=0}