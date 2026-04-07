using Microsoft.EntityFrameworkCore;
using Tubester.Abstractions.Account;
using Tubester.Domain;

namespace Tubester.Persistence.Account;

public sealed class AccountSettingsRepository(TubesterDb db) : IAccountSettingsRepository
{
    public async Task<AccountSettings?> GetByUserIdAsync(string userId, CancellationToken cancellationToken)
    {
        return await db.Set<AccountSettings>()
            .FirstOrDefaultAsync(s => s.UserId == userId, cancellationToken);
    }

    public async Task UpsertAsync(AccountSettings settings, CancellationToken cancellationToken)
    {
        var existing = await db.Set<AccountSettings>()
            .FirstOrDefaultAsync(s => s.UserId == settings.UserId, cancellationToken);

        if (existing is null)
        {
            db.Set<AccountSettings>().Add(settings);
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
