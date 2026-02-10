GOAL

Refactor the YouTube integration so that:

YouTubeService is no longer a singleton bound to a single configured user.

Instead, YouTube API calls are made using a per-user, token-based YouTubeService, created on demand using tokens from
our IUserTokenStore.

All YouTube operations (channels, playlists, videos, etc.) work per signed-in user, identified by our internal userId (
Google sub).

The end state should support multiple users, each with their own Google tokens, without relying on a global
YouTubeService instance.

CURRENT SITUATION (please inspect and confirm)

In the Tubester.Integration project we currently have something like:

A YouTubeClientFactory or YouTubeServiceFactory registered as singleton in DI:

builder.Services.AddSingleton<IYouTubeClientFactory, YouTubeClientFactory>();

That factory creates a single YouTubeService instance at startup, using configuration (client ID/secret/API key/etc.)
for one specific YouTube account.

IYouTubeIntegration methods use this singleton YouTubeService internally, so all calls effectively act on one global
channel/user.

The system used to assume “one YouTube account only”, which is no longer what we need.

We now have:

A User entity and UserTokens entity in Tubester.Persistence.

IUserRepository and IUserTokenStore in Tubester.Persistence.Users.

Tokens (access token, refresh token, expiresAt) are saved during Google OAuth login in AddCookieWithGoogle (
OnTicketReceived), keyed by userId = Google sub (ClaimTypes.NameIdentifier).

We’ve also added:

A per-user channel sync flow (e.g. IChannelSyncService.SyncChannelsForUserAsync(string userId, CancellationToken ct)).

A /channels/sync endpoint that enqueues Hangfire jobs for a specific userId.

WHAT I WANT YOU TO DO

1. Refactor the YouTube client creation to be per user (token-based)

Inspect the current YouTubeClientFactory / YouTubeServiceFactory and IYouTubeClientFactory (names may differ; search in
Tubester.Integration).

Change the design so that:

The factory no longer creates a single YouTubeService at startup that is reused for everyone.

Instead, it can create a YouTubeService for a given user, based on tokens stored in UserTokens.

I imagine something like this conceptually (don’t just stick to my naming, follow existing conventions):

IYouTubeClientFactory (or a new interface, if needed) with a method along the lines of:

“Get or create a YouTubeService for a particular userId”.

This method should:

Load tokens for userId via IUserTokenStore.

If no tokens or no refresh token: behave gracefully (throw, return null, or let the caller handle it – pick the pattern
that fits the current integration style).

Use our Google OAuth client ID/secret from IConfiguration (or existing config) plus the stored refresh token to
construct the appropriate Google credentials.

Ensure that if the access token is expired / near expiry and a refresh token exists, it can obtain a fresh access
token (follow or extend existing token-refresh patterns if they already exist).

Create a YouTubeService instance that uses these credentials.

Make sure this works in both:

API (per-request scope).

Hangfire jobs (scoped services resolved via DI inside the job methods).

The factory itself can remain singleton if all state is external and it just resolves scoped dependencies when needed,
or it can be scoped/transient – pick what fits current DI style best. The key is: the actual YouTubeService and
credentials must be per-user and per-call, not a single global instance.

2. Update IYouTubeIntegration to be per-user

Scan IYouTubeIntegration and its implementation.

Refactor it so that:

All relevant methods that talk to YouTube are per-user, i.e. they either:

accept a userId parameter, or

are explicitly “for current user” in your application service, which then passes userId down.

Inside IYouTubeIntegration implementation:

Use the new per-user YouTubeService factory to obtain a YouTubeService instance for the given userId.

Make YouTube API calls using that service.

Examples of methods that should become per-user:

“Get all channels for the user”

“Get playlists for the user”

“Sync channel details”, etc.

Anywhere the old singleton YouTubeService was used as a global context, change it to per-user usage.

3. Wire this into existing application services (Channel sync, etc.)

Find places where the YouTube integration is used from the Application layer, especially:

IChannelSyncService and its implementation.

Any other services that use IYouTubeIntegration.

Update them so that:

They work with a userId parameter – which they already should, in the per-user sync path (SyncChannelsForUserAsync(
userId, ct)).

They pass that userId down into IYouTubeIntegration, which then uses the per-user YouTubeService from the factory.

Make sure:

IChannelSyncService does not new up YouTubeService directly or access tokens itself – it should only coordinate:

which userId to work for,

which repositories to update,

and delegate actual API calls to IYouTubeIntegration.

4. Do not add an API → Integration reference

Keep the dependency graph as:

Tubester.Api → Tubester.Application → Tubester.Integration

If Warp previously added a direct project reference from API to Integration, remove that and route everything through
the Application layer (e.g. via Channel service / Channel sync service).

Controllers (e.g. ChannelsController) should call Application services, not IYouTubeIntegration directly.

5. Respect existing auth/user model

Use userId consistently:

This is the Google sub (ClaimTypes.NameIdentifier), already used as primary key in User and UserTokens.

Don’t switch to email as an identity key.

Use IUserTokenStore to retrieve tokens based on userId.

Assume:

Google is the only auth provider.

Tokens are stored per-user in UserTokens and are updated on login in AddCookieWithGoogle.

6. Tests / sanity checks

Update or add tests where reasonable, especially:

Any existing tests that use IYouTubeIntegration or channel sync should still compile and pass.

Where we have integration tests using CapturingBackgroundJobClient, make sure channel sync jobs still execute via
IChannelSyncService with a userId, and that internally IYouTubeIntegration is now per-user.

No need to mock Google API itself in great detail; just ensure DI wiring, method signatures, and internal collaboration
make sense and compile.

IMPORTANT CONSTRAINTS

Don’t break existing public API endpoints (/auth/me, /channels/sync, etc.). Their contracts should remain the same;
we’re only changing how integration is implemented internally.

Keep the architecture layered:

API → Application → Integration → YouTube API.

Per-user/personalization must be done using our userId (sub), not email.

Make minimal necessary changes to introduce per-user YouTubeService, but do it cleanly and consistently across
Tubester.Integration and the dependent Application services.

When you’re done, the system should:

For a given userId, obtain that user’s tokens from IUserTokenStore,

Use them to build a per-user YouTubeService,

Use that service for all YouTube API calls for that user,

Without relying on a singleton global YouTubeService created at startup.