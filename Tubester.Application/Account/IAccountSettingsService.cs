namespace Tubester.Application.Account;

public interface IAccountSettingsService
{
    Task<AccountSettingsDto> GetOrCreateAsync(string userId, CancellationToken cancellationToken);

    Task<AccountSettingsDto> UpdateAsync(string userId, UpdateAccountSettingsRequest request,
        CancellationToken cancellationToken);
}
