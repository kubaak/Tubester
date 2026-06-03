namespace Tubester.Abstractions.Users;

public interface IUserRepository
{
    Task<User> UpsertUserAsync(
        string userId,
        string? email,
        string? name,
        string? picture,
        DateTimeOffset loginAt,
        CancellationToken cancellationToken);

    Task UpdateUserAsync(User user, CancellationToken cancellationToken);

    Task<User?> GetByIdAsync(string userId, CancellationToken cancellationToken);

    Task<User?> GetByIdForUpdateAsync(string userId, CancellationToken cancellationToken);

    Task MarkAsDeletedAsync(string userId, DateTimeOffset deletedAt, CancellationToken cancellationToken);

    Task RestoreFromFreshLoginAsync(string userId, string? email, string? name, string? picture, DateTimeOffset reactivatedAt, CancellationToken cancellationToken);
}
