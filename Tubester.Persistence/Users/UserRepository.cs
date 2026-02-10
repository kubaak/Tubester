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
}
