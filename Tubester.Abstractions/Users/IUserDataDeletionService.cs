namespace Tubester.Abstractions.Users;

/// <summary>
/// Service for deleting user data and all Google/YouTube-derived content.
/// Used to support Google OAuth app review requirements for account deletion.
/// </summary>
public interface IUserDataDeletionService
{
    /// <summary>
    /// Deletes all user-owned data and all data obtained from or derived from Google/YouTube APIs.
    /// The operation preserves only the minimal technical User row required by the current database model.
    /// </summary>
    /// <param name="userId">The Google subject ID / primary key of the user.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task DeleteUserDataAsync(string userId, CancellationToken cancellationToken);
}