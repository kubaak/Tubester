I want to refactor Google/YouTube auth to the following model:

Phase 1:

On login, we only get a read-only YouTube token,

we do not store tokens in the database (yet),

for each request we create a YouTubeService on the fly from the current user’s token,

and when a user wants to perform an edit (post comment, edit video), we trigger a separate login flow with write scope (
youtube.force-ssl) and build a YouTubeService from that token only for that operation (no persistence).

We will keep the current UserToken / UserTokenData / IUserTokenStore structures, but we will stop using them in this
phase. They are for later when we introduce optional persistent write tokens.

Please modify the solution accordingly.

1. Read-only login flow (default app login)

In Tubester.Api.Extensions.ServiceCollectionExtensions.AddCookieWithGoogle:

Configure the main Google auth flow used by /auth/login/google to request only read-only scopes:

openid

profile

email

YouTube read-only scope (YouTubeService.Scope.YoutubeReadonly or equivalent), NOT YoutubeForceSsl.

Keep SaveTokens = true so that:

the access token and refresh token are stored only in the authentication cookie / auth properties, not in the database.

In the OnTicketReceived (or similar) auth event:

Continue to upsert the User (id, email, name, picture, last login) via IUserRepository, as before.

Remove or comment out any code that writes to IUserTokenStore or persists tokens.

Do not call IUserTokenStore.Upsert... anywhere in this flow.

Ensure this flow is used for normal app login and for all read-only features (listing channels, videos, comments,
generating drafts, etc.).

2. Accessing the read-only token per request

We now want to create YouTubeService on the fly per request using the token stored in the auth ticket (cookie), not the
DB.

Implement a helper in the Api layer (or Abstractions + Api), e.g. ICurrentUserTokenAccessor:

It should:

Use IHttpContextAccessor (or HttpContext.RequestServices.GetRequiredService<IAuthenticationService> / GetTokenAsync) to
obtain the current authenticated principal and its tokens.

Provide a method like:

Task<string?> GetAccessTokenAsync(CancellationToken ct);

This method should read the access_token from the current authentication properties (e.g. HttpContext.GetTokenAsync("
access_token")).

Add a method that returns a read-only YouTubeService for the current user, e.g.:

Task<YouTubeService> CreateReadOnlyServiceAsync(CancellationToken ct);

It should:

get the access token via GetAccessTokenAsync,

fail clearly if no token is available (user not logged in),

create a GoogleCredential or UserCredential from the in-memory access token with the read-only scope,

construct and return a YouTubeService for read operations.

Register this helper in DI (Api) and use it from controllers or from a thin Application-level service if needed.

Important: do not use IUserTokenStore anywhere in this helper.

3. Read integration: switch to per-request YouTubeService

In Tubester.Integration and Application code that currently uses YouTubeServiceFactory / IYouTubeIntegration for read
operations:

Replace usages that rely on stored tokens with code that takes a YouTubeService built from the current user token.

Concretely:

Either:

Change IYouTubeIntegration so that read methods accept a YouTubeService instance (or a GoogleCredential) instead of a
userId, and let Api/controllers provide the service using the ICurrentUserTokenAccessor.

Or:

Inject ICurrentUserTokenAccessor into YouTubeIntegration and let YouTubeIntegration itself call
CreateReadOnlyServiceAsync(ct) internally for each read method.

Ensure that all read-only operations (sync channels, videos, comments, etc.) get their YouTubeService from the current
request’s access token, not from the DB.

Keep the existing YouTubeServiceFactory / IUserTokenStore types if used elsewhere, but stop using them for normal read
flows in this phase. If necessary, clearly mark them as “reserved for future persistent-token phase”.

4. Write (edit) operations: separate login flow with youtube.force-ssl

We are not fully implementing online-write flow here, but we need the basic structure so the design is consistent.

In AuthController (or a dedicated controller under auth):

Introduce a separate login endpoint for write operations, e.g.:

GET /auth/login/google-write (or a mode=write parameter on the existing login).

Configure it so that:

It triggers a Google challenge with additional scope: YouTubeService.Scope.YoutubeForceSsl (or equivalent write scope).

SaveTokens = true is still fine, but:

Do not persist these tokens to DB (IUserTokenStore).

They live only in the auth ticket / cookie.

In the OnTicketReceived (or callback logic) for this write login:

Extract the write-capable access_token from auth properties.

For now, just ensure we can access that token so we can later build a YouTubeService for write actions when the user is
online.

Do not store refresh tokens with write scopes in the DB.

At this point, you don’t need to implement the full “post reply” flow; just ensure:

There is a clear way to start a write-specific login,

After this flow, the auth cookie holds a token with youtube.force-ssl scope that can be used in a per-request service
factory similar to the read-only one.

5. Clean up any DB token usage (without deleting the types)

Search for all usages of IUserTokenStore, UserTokenStore, UserTokens / ChannelTokens across the solution.

For this phase:

Remove or comment out usages of IUserTokenStore from:

Authentication event handlers (OnTicketReceived, etc.),

YouTubeServiceFactory (if it uses DB tokens),

Background jobs (PostApprovedRepliesJob, etc.) that rely on stored tokens.

Leave the types (IUserTokenStore, UserToken/ChannelToken EF entity) and their implementations in place, but unused for
now. They will be used in a later phase when we implement persistent write tokens as an opt-in.

Ensure the app still compiles and all DI registrations are consistent (if IUserTokenStore is no longer used anywhere,
you can either leave its registration or remove it; minimum requirement is that nothing uses it for current logic).

6. Tests and compilation

Adjust existing tests to match the new behavior:

Auth integration tests should no longer expect any DB writes to a token store.

Any tests that previously asserted UserTokenStore behavior should be either:

marked as skipped with a TODO for “persistent tokens phase”, or

updated to reflect that tokens are no longer persisted.

Make sure all projects compile:

Api, Application, Integration, Worker should no longer depend on stored tokens for read operations.

Any code that still tries to use IUserTokenStore for live behavior should be refactored as above.

Summary of core changes:

Login: read-only scopes, no token persistence, but tokens available in auth cookie.

Read operations: build YouTubeService per request from current user’s access token using a helper like
ICurrentUserTokenAccessor.

Write operations: set up a separate login flow with youtube.force-ssl scope, no DB persistence yet; just ensure we can
access the stronger token per request.

UserToken structures: kept in code but not used for current runtime behavior; reserved for a future phase where we may
optionally persist write tokens for background jobs.

Apply these changes incrementally, following existing styles and conventions.