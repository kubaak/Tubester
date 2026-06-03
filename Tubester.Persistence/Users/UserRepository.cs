using Microsoft.EntityFrameworkCore;
using Tubester.Abstractions.Users;

namespace Tubester.Persistence.Users;

public sealed class UserRepository(TubesterDb databaseContext) : IUserRepository
{
    public async Task<User> UpsertUserAsync(
        string userId,
        string? email,
        string? name,
        string? picture,
        DateTimeOffset loginAt,
        CancellationToken cancellationToken)
    {
        var existingUser = await databaseContext.Users
            .FirstOrDefaultAsync(user => user.Id == userId, cancellationToken);

        if (existingUser is null)
        {
            var createdUser = User.Create(userId, email, name, picture, loginAt);
            await databaseContext.Users.AddAsync(createdUser, cancellationToken);
            await databaseContext.SaveChangesAsync(cancellationToken);
            return createdUser;
        }

        existingUser.UpdateProfile(email, name, picture, loginAt);
        await databaseContext.SaveChangesAsync(cancellationToken);
        return existingUser;
    }

    public async Task<User?> GetByIdAsync(string userId, CancellationToken cancellationToken)
    {
        var user = await databaseContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(entity => entity.Id == userId, cancellationToken);

        return user;
    }

    public async Task<User?> GetByIdForUpdateAsync(string userId, CancellationToken cancellationToken)
    {
        var user = await databaseContext.Users
            .FirstOrDefaultAsync(entity => entity.Id == userId, cancellationToken);

        return user;
    }

    public async Task UpdateUserAsync(User user, CancellationToken cancellationToken)
    {
        databaseContext.Users.Update(user);
        await databaseContext.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkAsDeletedAsync(string userId, DateTimeOffset deletedAt, CancellationToken cancellationToken)
    {
        await databaseContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE "Users"
            SET "Email" = NULL,
                "Name" = NULL,
                "Picture" = NULL,
                "IsDeleted" = true,
                "DeletedAt" = {deletedAt},
                "ReactivatedAt" = NULL
            WHERE "Id" = {userId}
            """,
            cancellationToken);
    }

    public async Task RestoreFromFreshLoginAsync(
        string userId,
        string? email,
        string? name,
        string? picture,
        DateTimeOffset reactivatedAt,
        CancellationToken cancellationToken)
    {
        await databaseContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE "Users"
            SET "Email" = {email},
                "Name" = {name},
                "Picture" = {picture},
                "LastLoginAt" = {reactivatedAt},
                "IsDeleted" = false,
                "ReactivatedAt" = {reactivatedAt},
                "IsNew" = false
            WHERE "Id" = {userId}
            """,
            cancellationToken);
    }
}
