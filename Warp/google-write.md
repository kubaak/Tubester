Current state:

Cookie-based auth with Google configured in AddCookieWithGoogle.

Login flow for read-only YouTube access:

Uses a Google handler with YouTubeService.Scope.YoutubeReadonly.

Uses SaveTokens = true so an access_token is stored in auth properties.

Enriches the principal with channel claims (yt_channel_id, yt_channel_title, yt_channel_picture).

Auth cookie is the single default scheme (Cookies).

/api/auth/me reads user + channel info from claims only.

Read-only YouTube access is implemented via ICurrentUserTokenAccessor / CreateReadOnlyServiceAsync and
IYouTubeIntegration.

Now I want to introduce a second consent step for write access (edit operations), with this design:

One cookie, one stored access token (no separate write token key).

Initially, the token has read-only scope.

When the user explicitly goes through a “write” Google flow, we:

get a new access_token with write scopes (e.g. YoutubeForceSsl),

overwrite the stored access_token in the cookie,

add a claim yt_write_granted = "true" to the principal.

Write endpoints require a policy that checks yt_write_granted.

All YouTube calls use the single access token; after write-consent it has write scopes.

Please implement this, keeping the architecture and style consistent.

1. Add a second Google auth handler for write scopes

In YouTubester.Api.Extensions.ServiceCollectionExtensions.AddCookieWithGoogle:

Keep the existing Google handler as the read-only login:

Give it a clear name (if not already), e.g. "GoogleRead":

.AddGoogle("GoogleRead", o =>
{
o.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
o.Scope.Add(YouTubeService.Scope.YoutubeReadonly);
o.SaveTokens = true;
// existing channel-claims logic stays here
})

This is used by /api/auth/login/google.

Add a second Google handler, "GoogleWrite", for edit scopes:

.AddGoogle("GoogleWrite", o =>
{
o.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
o.Scope.Add(YouTubeService.Scope.YoutubeForceSsl); // or appropriate write scope(s)
o.SaveTokens = true;
o.CallbackPath = "/api/auth/google/write/callback";

    // Ensure we still request profile/email as needed (same as read)
    // and reuse any common options (ClientId, ClientSecret, etc.)

})

In the "GoogleWrite" handler’s events (OnTicketReceived or OnCreatingTicket):

Get the ClaimsIdentity:

var identity = (ClaimsIdentity)context.Principal!.Identity!;

Add a claim to mark write capability:

identity.AddClaim(new Claim("yt_write_granted", "true"));

Do not create separate yt_write_access_token keys.
Just let SaveTokens = true overwrite the existing access_token with the new, write-scoped token.

Make sure both Google handlers share:

Same ClientId / ClientSecret.

Same SignInScheme (the cookie).

Same basic user + channel enrichment logic where appropriate (if you want channel claims updated on write-consent too,
reuse that code; otherwise, read-only handler can handle initial channel claims).

2. Add an endpoint to start the write-consent flow

In YouTubester.Api.AuthController ([Route("api/auth")]):

Add a new endpoint:

[HttpGet("google/write/start")]
[Authorize] // user should be logged in already; or AllowAnonymous if you prefer
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public IActionResult StartWriteConsent([FromQuery] string? returnUrl = "/")
{
if (string.IsNullOrWhiteSpace(returnUrl) || !Url.IsLocalUrl(returnUrl))
{
returnUrl = "/";
}

    var props = new AuthenticationProperties
    {
        RedirectUri = returnUrl
    };

    return Challenge(props, "GoogleWrite");

}

Flow:

Frontend calls /api/auth/google/write/start?returnUrl=... when user clicks “Enable editing”.

User goes to Google, grants write scopes.

"GoogleWrite" handler runs on callback:

SaveTokens stores the new write-scoped access_token in the cookie.

Claim yt_write_granted=true is added.

User is redirected to returnUrl (FE dashboard for example).

No DB persistence of tokens, only cookie storage.

3. Add an authorization policy for write endpoints

In your API service setup (where you call AddAuthorization):

Add a policy like:

services.AddAuthorization(options =>
{
options.AddPolicy("RequiresYouTubeWrite", policy =>
{
policy.RequireAuthenticatedUser();
policy.RequireClaim("yt_write_granted", "true");
});
});

Do not change the default scheme; still use the cookie scheme.

4. Protect YouTube edit endpoints with the new policy

Find controllers that perform YouTube write operations, e.g.:

VideosController.CopyTemplate (POST)

Any endpoint that posts replies / comments

Any endpoint that updates video metadata

On these endpoints, add:

[Authorize(Policy = "RequiresYouTubeWrite")]

Read-only endpoints remain with the existing [Authorize] (no policy) and will work even if yt_write_granted is absent.

5. Token accessor: keep a single access token, just ensure it works in both phases

You already have a token accessor (e.g. CurrentUserTokenAccessor) that:

Uses IAuthenticationService.AuthenticateAsync(...),

Reads access_token from AuthenticationProperties,

Creates a YouTube read-only service with YouTubeService.Scope.YoutubeReadonly.

Please adjust it to be neutral with respect to the scope:

Rename methods if needed:

CreateReadOnlyServiceAsync → CreateYouTubeServiceAsync (or similar),

Or keep the name but make sure it can handle both read-only and write scopes (the scope is effectively determined by the
token now).

Implementation:

Still read access_token from authenticateResult.Properties.GetTokenValue("access_token").

Still create a GoogleCredential.FromAccessToken(accessToken).

Apply required scopes for your integration:

You can still use CreateScoped(YouTubeService.Scope.YoutubeReadonly), but once the token has a superset scope (
YoutubeForceSsl), that’s fine.

Alternatively, you can set YoutubeForceSsl as the default scope for the client if you want; the underlying token already
has that permission after write-consent. Keep it consistent with how Integration expects scopes.

All API calls (read + write) use the same accessor and the same access_token:

Before write-consent → token only has read scope; write endpoints are blocked by policy anyway.

After write-consent → token has write scope; read endpoints still work, and write endpoints are now allowed by policy.

There is no second token in properties and no DB storage.

6. (Optional) Expose write status in /api/auth/me

Update /api/auth/me to include a hasWriteAccess flag:

Read the claim:

var hasWriteAccess = User.HasClaim("yt_write_granted", "true");

Include it in the JSON:

return Ok(new
{
name,
email,
sub = subject,
channelId,
channelTitle,
picture,
hasWriteAccess
});

This helps the frontend decide when to show an “Enable editing” button vs. directly calling write endpoints.

7. Summary of constraints

One cookie scheme, no extra cookie.

Two Google handlers:

"GoogleRead" for initial login (read-only scope).

"GoogleWrite" for explicit edit-consent (write scope).

One stored access token in auth properties:

Initially read-only,

Overwritten by the write-scoped token after write-consent.

A claim yt_write_granted=true gates dangerous endpoints via policy.

No DB persistence of tokens in this phase.

Please implement these changes incrementally, keeping the existing YouTubester code conventions and dependency
structure.