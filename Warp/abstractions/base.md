We are refactoring the UserTokens handling to follow this design:

Tubester.Abstractions defines a DTO + interface describing user tokens.

Tubester.Persistence contains the EF entity + DbContext mapping and implements the abstraction.

Tubester.Integration uses only the abstraction (DTO + interface), not the Domain or Persistence types.

The Domain project should no longer contain UserToken – tokens are treated as an auth/infra concern, not core domain.

I will describe the desired end state; please refactor the solution accordingly.

1. Token DTO + interface in Abstractions (Option 2)

In the Tubester.Abstractions project, under a namespace like Tubester.Abstractions.Auth, create:

namespace Tubester.Abstractions.Auth;

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

Tubester.Abstractions must not reference EF or any persistence-related libraries. This DTO and interface are pure
contracts.

Ensure all projects that need token access (Api, Application, Integration, Persistence, Worker) reference
Tubester.Abstractions.

2. Move the EF entity UserToken into Persistence

Currently, there is a UserToken type in the Domain namespace (e.g. Tubester.Domain.UserToken) and IUserTokenStore in
Tubester.Persistence.Users that returns it.

Refactor to:

Remove UserToken from the Domain project:

Find the UserToken class (probably in Tubester.Domain) and move it to Tubester.Persistence under a namespace like
Tubester.Persistence.Entities or Tubester.Persistence.Users.

This class will now be treated as a pure persistence entity.

Ensure TubesterDb (DbContext) in Tubester.Persistence still has a DbSet<UserToken> and that it points to the moved
entity type (update namespace/usings).

If there are any Domain references to UserToken, replace them:

Domain should no longer depend on UserToken.

Any logic that truly needs token data should be moved to Application or Persistence layers, or updated to use
IUserTokenStore where appropriate.

3. Implement IUserTokenStore in Persistence

In Tubester.Persistence:

Create a concrete implementation, e.g.:

using Microsoft.EntityFrameworkCore;
using Tubester.Abstractions.Auth;

namespace Tubester.Persistence.Users;

public sealed class UserTokenStore : IUserTokenStore
{
private readonly TubesterDb _db;

    public UserTokenStore(TubesterDb db)
    {
        _db = db;
    }

    public async Task<UserTokenData?> GetAsync(string userId, CancellationToken cancellationToken)
    {
        var entity = await _db.UserTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);

        if (entity is null)
        {
            return null;
        }

        return new UserTokenData
        {
            UserId = entity.UserId,
            AccessToken = entity.AccessToken,
            RefreshToken = entity.RefreshToken,
            ExpiresAt = entity.ExpiresAt
        };
    }

    public async Task UpsertAsync(
        string userId,
        string? accessToken,
        string? refreshToken,
        DateTimeOffset? expiresAt,
        CancellationToken cancellationToken)
    {
        var entity = await _db.UserTokens
            .FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);

        if (entity is null)
        {
            entity = new UserToken
            {
                UserId = userId,
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                ExpiresAt = expiresAt
            };

            await _db.UserTokens.AddAsync(entity, cancellationToken);
        }
        else
        {
            entity.AccessToken = accessToken;
            entity.RefreshToken = refreshToken;
            entity.ExpiresAt = expiresAt;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

}

Remove any old IUserTokenStore interface from Persistence that:

lived under Tubester.Persistence.Users and

returned Domain.UserToken.

In your DI configuration (Api/Worker composition root), register the new implementation:

services.AddScoped<IUserTokenStore, UserTokenStore>();

4. Update the Google auth pipeline to use the new abstraction

Find the place where tokens are saved during Google login, e.g. in AddCookieWithGoogle (OnTicketReceived event) or
similar:

It previously may have used IUserTokenStore.UpsertGoogleTokenAsync(...) or similar.

Refactor it to use IUserTokenStore.UpsertAsync(...) from Abstractions:

var userTokenStore = context.HttpContext.RequestServices.GetRequiredService<IUserTokenStore>();

await userTokenStore.UpsertAsync(
userId,
accessToken,
refreshToken,
expiresAt,
cancellationToken);

Make sure you pass context.HttpContext.RequestAborted as the cancellation token.

Remove any references to the old IUserTokenStore interface from Persistence.

5. Update YouTubeServiceFactory to use UserTokenData from Abstractions

In Tubester.Integration, update YouTubeServiceFactory:

Replace any dependency on Persistence or Domain token types with the new abstraction:

Add:

using Tubester.Abstractions.Auth;

Change the constructor to:

public sealed class YouTubeServiceFactory(
IOptions<YouTubeAuthOptions> options,
IUserTokenStore userTokenStore,
ILogger<YouTubeServiceFactory> logger) : IYouTubeServiceFactory

Where it previously called GetGoogleTokenAsync, change to:

var userTokens = await userTokenStore.GetAsync(userId, cancellationToken);

Adjust the logic to use UserTokenData:

userTokens.AccessToken

userTokens.RefreshToken

userTokens.ExpiresAt

Update the helper:

private static TokenResponse BuildTokenResponse(UserTokenData userToken)
{
long? expiresInSeconds = null;

    if (userToken.ExpiresAt.HasValue)
    {
        var now = DateTimeOffset.UtcNow;
        var remaining = userToken.ExpiresAt.Value - now;
        if (remaining > TimeSpan.Zero)
        {
            expiresInSeconds = (long)remaining.TotalSeconds;
        }
    }

    return new TokenResponse
    {
        AccessToken = userToken.AccessToken,
        RefreshToken = userToken.RefreshToken,
        ExpiresInSeconds = expiresInSeconds
    };

}

Ensure YouTubeServiceFactory does not reference:

Tubester.Persistence.*

the old domain UserToken type.

If there was a project reference from Integration → Persistence, remove it from the .csproj.

6. Clean up and verify

Domain project:

Must not contain UserToken anymore.

Must not reference Abstractions.

Abstractions project:

Contains UserTokenData + IUserTokenStore (no EF, no Google SDK).

Persistence project:

Contains UserToken EF entity and UserTokenStore implementation.

References Abstractions (for IUserTokenStore / UserTokenData).

Integration project:

Depends on Abstractions (IUserTokenStore, UserTokenData) and its own configuration/exceptions.

Does not reference Persistence.

Build the solution and run tests (dotnet test).

Especially verify:

Auth flow still saves tokens correctly.

YouTube integration still constructs YouTubeService correctly.

Jobs that rely on YouTubeServiceFactory still run.

When implementing, infer exact names/namespaces from the existing code (e.g. UserTokens vs UserToken, namespaces under
Tubester.Persistence.Users, etc.) and align with current coding style.