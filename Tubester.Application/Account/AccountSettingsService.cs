using Tubester.Abstractions.Account;
using Tubester.Abstractions.Credits;
using Tubester.Domain;

namespace Tubester.Application.Account;

public sealed class AccountSettingsService(
    IAccountSettingsRepository accountSettingsRepository,
    ICreditsStore creditsStore,
    IDateTimeOffsetProvider dateTimeOffsetProvider) : IAccountSettingsService
{
    public async Task<AccountSettingsDto> GetOrCreateAsync(string userId, CancellationToken cancellationToken)
    {
        var settings = await accountSettingsRepository.GetByUserIdAsync(userId, cancellationToken);

        if (settings is null)
        {
            var nowUtc = dateTimeOffsetProvider.GetUtcNowDateTimeOffset();
            settings = AccountSettings.CreateDefault(userId, nowUtc);
            await accountSettingsRepository.UpsertAsync(settings, cancellationToken);
        }

        var subscription = await creditsStore.GetSubscriptionSummaryAsync(userId, cancellationToken);

        return ToDto(settings, subscription);
    }

    public async Task<AccountSettingsDto> UpdateAsync(string userId, UpdateAccountSettingsRequest request,
        CancellationToken cancellationToken)
    {
        var settings = await accountSettingsRepository.GetByUserIdAsync(userId, cancellationToken);

        var nowUtc = dateTimeOffsetProvider.GetUtcNowDateTimeOffset();

        if (settings is null)
        {
            settings = AccountSettings.CreateDefault(userId, nowUtc);
            await accountSettingsRepository.UpsertAsync(settings, cancellationToken);
        }

        settings.Apply(request.PreferredTheme, request.PreferredLanguage, nowUtc);
        await accountSettingsRepository.UpsertAsync(settings, cancellationToken);

        var subscription = await creditsStore.GetSubscriptionSummaryAsync(userId, cancellationToken);

        return ToDto(settings, subscription);
    }

    private static AccountSettingsDto ToDto(AccountSettings settings, SubscriptionSummaryDto? subscription)
    {
        return new AccountSettingsDto(
            settings.UserId,
            settings.PreferredTheme,
            settings.PreferredLanguage,
            settings.UpdatedAtUtc,
            subscription);
    }
}
