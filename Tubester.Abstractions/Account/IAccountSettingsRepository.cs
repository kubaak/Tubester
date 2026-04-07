using Tubester.Domain;

namespace Tubester.Abstractions.Account;

public interface IAccountSettingsRepository
{
    Task<AccountSettings?> GetByUserIdAsync(string userId, CancellationToken cancellationToken);

    Task UpsertAsync(AccountSettings settings, CancellationToken cancellationToken);
}
