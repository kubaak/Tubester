You are Warp AI working in my Tubester repo (ASP.NET Core API + Hangfire + YouTube integration + SQLite for now,
PostgreSQL later).
GOAL
Finish the implementation of:

Per-user token persistence for Google OAuth (YouTube access), with a clean split between:

a User entity (business data: id, email, name, picture),

a UserTokens entity (secrets: tokens, expiry).

A per-user channel sync flow, so the currently signed-in user’s channels are synced via a background job using tokens
from persistence.

A relational link between users and channels via Channels.UserId (foreign key to Users.Id), so each channel is
associated with the user that owns it.

Basic tests to verify the behavior end-to-end from the API side.

Use existing patterns in the repo for DI, EF Core, Hangfire, and integration tests.

CONTEXT
Key places in the repo (please scan them first):

API entry: Tubester.Api/Program.cs

Auth configuration: Tubester.Api/Extensions/ServiceCollectionExtensions.AddCookieWithGoogle(...)

Auth controller: Tubester.Api/AuthController.cs

Endpoints: /auth/login/google, /auth/logout, /auth/me

/auth/me returns name, email, sub, picture from claims.

Channel-related application services: Tubester.Application and Tubester.Application.Channels

Look for IChannelSyncService and its implementation.

Persistence: Tubester.Persistence (TubesterDb context, entities, configurations, migrations).

Hangfire: configured in API + worker; tests use CapturingBackgroundJobClient.

Integration tests infrastructure:

Tubester.IntegrationTests/TestHost/ApiTestWebAppFactory.cs

Tubester.IntegrationTests/TestHost/MockAuthenticationExtensions.cs

Tubester.IntegrationTests/TestHost/TestFixture.cs

Existing tests for /auth/me and auth in general.

Auth specifics:

Single provider: Google OAuth (YouTube is the only platform; no multi-provider support needed).

Cookie auth for session; Google external login for identity.

We use ClaimTypes.NameIdentifier (“sub”) as the stable user identifier.

SQLite is used now; later we plan to migrate to PostgreSQL and possibly separate schemas/DBs for tokens vs general app
data. For now:

keep everything in a single DB file / default schema,

but structure entities and mappings so that moving UserTokens (and possibly other sensitive tables) to a different
schema later will be easy.

WHAT TO IMPLEMENT

1) Introduce User and UserTokens entities
   Add two EF Core entities to Tubester.Persistence:

User:

Represents an app user (a Google account).

Primary key: Id (string) = Google sub (from ClaimTypes.NameIdentifier).

Properties for:

Email (last-seen email from Google; mutable, not identity)

Name

Picture

CreatedAt

LastLoginAt (nullable)

Navigation: one-to-one relationship to UserTokens.

UserTokens:

Represents secrets needed to call YouTube on this user’s behalf.

Primary key: UserId (string), FK to User.Id.

Navigation back to User.

Properties:

RefreshToken (nullable string)

AccessToken (nullable string; optional to use)

ExpiresAt (nullable DateTimeOffset for access token expiry)

Update TubesterDb:

Add DbSet<User> and DbSet<UserTokens>.

Configure a one-to-one relationship between User and UserTokens via fluent API.

Keep both tables in the same schema for now, but treat them as separate tables in model configuration so that later we
can easily move UserTokens to another schema/DB when migrating to PostgreSQL.

Add an EF Core migration that creates these tables in SQLite and updates the test harness to ensure DB creation works.

2) Add UserId foreign key to Channels
   Extend the existing Channel entity and mapping:

Add a UserId property (string, non-nullable) to the Channel entity.

Add a navigation from Channel to User (optional, but recommended).

Configure an EF Core relationship:

User 1 —— N Channel (Channel.UserId FK → User.Id).

Ensure that channel-related code in the app (sync logic, repositories) is updated to always set UserId for every channel
row based on the currently signed-in user whose channels are being synced.

Add/modify migrations to:

Add the UserId column to the Channels table.

Add the foreign key constraint Channels.UserId → Users.Id in SQLite.

The intended model is:

One app User (Google account) can have many Channels.

We are not supporting many-to-many (multi-user access to the same channel) at this stage.

3) Create a small persistence abstraction for users and tokens
   Introduce appropriate interfaces in Application or Persistence (follow your existing patterns), for example:

IUserRepository (or similar) that can:

Upsert a User by Id (sub) with basic fields (email, name, picture, timestamps).

Optionally expose queries if you need them later.

IUserTokenStore (or similar) that can:

SaveGoogleTokensAsync(userId, email, accessToken, refreshToken, expiresAt, ct) – upsert tokens for a given user, and
also keep the email in sync if useful.

GetGoogleTokensAsync(userId, ct) – retrieve token data for a user.

Key points:

Use userId = Google sub as the identity key everywhere.

Treat email as mutable and purely informational.

Structure the code so that UserTokens can later be moved to a different schema/DB without having to rewrite the
application layer.

Register these services in DI in Program.cs or in an appropriate DI extension so they are available to:

Auth pipeline (AddCookieWithGoogle)

Channel sync services

Background jobs.

4) Capture tokens and basic user info after Google login
   Update the Google auth configuration in AddCookieWithGoogle:

Ensure SaveTokens = true for the Google authentication options so tokens are stored in AuthenticationProperties.

Configure Google OAuth events (e.g. OnTicketReceived):

Extract:

userId from ClaimTypes.NameIdentifier,

email, name, picture from claims,

access_token, refresh_token, expires_at from token properties.

Use IUserRepository to:

Upsert the User row:

Id = userId

update Email, Name, Picture

update LastLoginAt

set CreatedAt only on first insert.

Use IUserTokenStore to:

Upsert UserTokens for this userId with the new token values.

Behavior constraints:

External behavior of /auth/login/google should remain the same from client point of view (redirect to Google, then
back).

/auth/me should continue to work without changes for the client; it should still return name, email, sub, picture from
claims.

Tokens should be stored defensively:

Keep properties nullable to allow for scenarios where some values are missing.

Do not introduce multi-provider fields (e.g., Provider) now; assume Google-only.

5) Add a per-user channel sync endpoint
   Expose an endpoint in the API (e.g., ChannelsController) that triggers a background channel sync for the currently
   signed-in user:

[ApiController], [Route("channels")], [Authorize].

Endpoint: POST /channels/sync.

Behavior:

Extract userId from ClaimTypes.NameIdentifier.

If missing → return 401.

Use IBackgroundJobClient (Hangfire) to enqueue a job on IChannelSyncService that will sync channels for this userId.

Return 202/Accepted with a simple JSON payload indicating that sync was scheduled.

This endpoint is intended to be called by the client after login (or on demand) to sync the channels of the signed-in
user.

6) Implement per-user channel sync using persisted tokens
   Make sure IChannelSyncService has a method like:

Task SyncChannelsForUserAsync(string userId, CancellationToken ct = default);

Implement it in the service class by:

Using IUserTokenStore to retrieve stored tokens for the given userId.

If there are no tokens or no refresh token:

Log a warning and exit gracefully.

Use the existing YouTube integration (e.g., IYouTubeIntegration, IYouTubeClientFactory or similar) to:

Obtain a valid access token from the stored refresh token (add or reuse token refresh logic as needed).

Fetch the list of channels for that user from YouTube.

For each channel:

Upsert it into the Channels table.

Ensure Channel.UserId is set to the userId passed into the sync method.

Reuse existing patterns in the codebase for:

API calls to YouTube,

paging,

error handling,

upserting channels.

The intent: channel data is always associated to the correct user via Channels.UserId, and background jobs only need to
know the userId, not any tokens directly.

7) Tests
   Leverage the existing integration test infrastructure:

For authenticated tests using ApiTestWebAppFactory and mock auth:

Add an integration test for POST /channels/sync that:

Uses the mock authenticated user (with a known sub).

Calls /channels/sync.

Asserts:

Response status is 202/Accepted.

CapturingBackgroundJobClient has enqueued a job for IChannelSyncService.SyncChannelsForUserAsync with the expected
userId (the mocked sub).

For token/user persistence, add tests (unit or integration) that:

Given a fake auth principal and token properties, calling the logic used by the Google OAuth event:

Creates or updates a User row with the expected Id, Email, Name, Picture, timestamps.

Creates or updates a UserTokens row for that UserId with the correct token values.

Existing auth tests (/auth/me, logout, etc.) should continue to pass with minimal or no changes.

IMPORTANT CONSTRAINTS / STYLE

Google is the only auth provider; do not add multi-provider abstractions (e.g., no “Provider” column).

Continue to key everything by sub (ClaimTypes.NameIdentifier) as User.Id and UserTokens.UserId.

Email is not an identity key; treat it as mutable, display-only data.

Keep everything in a single SQLite database / schema for now, but:

Keep User and UserTokens as separate tables/entities.

Make UserTokens mapping easy to move later (e.g., to a secrets schema in Postgres).

Do not change the public contract of existing auth endpoints.

Follow naming/DI/logging patterns already used in the repo.

Ensure dotnet test passes when you’re done.


