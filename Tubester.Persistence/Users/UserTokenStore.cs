using Microsoft.EntityFrameworkCore;
using Tubester.Abstractions.Auth;

namespace Tubester.Persistence.Users;

public sealed class UserTokenStore(TubesterDb databaseContext) : IUserTokenStore
{
    public async Task<UserTokenData?> GetAsync(string userId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("User id must be a non-empty string.", nameof(userId));
        }

        var entity = await databaseContext.UserTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(existingEntity => existingEntity.UserId == userId, cancellationToken);

        if (entity is null)
        {
            return null;
        }

        var userTokenData = new UserTokenData
        {
            UserId = entity.UserId,
            AccessToken = entity.AccessToken,
            RefreshToken = entity.RefreshToken,
            ExpiresAt = entity.ExpiresAt
        };

        return userTokenData;
    }

    public async Task UpsertAsync(
        string userId,
        string? accessToken,
        string? refreshToken,
        DateTimeOffset? expiresAt,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("User id must be a non-empty string.", nameof(userId));
        }

        var entity = await databaseContext.UserTokens
            .FirstOrDefaultAsync(existingEntity => existingEntity.UserId == userId, cancellationToken);

        if (entity is null)
        {
            entity = UserToken.Create(userId, refreshToken, accessToken, expiresAt);
            await databaseContext.UserTokens.AddAsync(entity, cancellationToken);
        }
        else
        {
            entity.UpdateTokens(refreshToken, accessToken, expiresAt);
        }

        await databaseContext.SaveChangesAsync(cancellationToken);
    }
}