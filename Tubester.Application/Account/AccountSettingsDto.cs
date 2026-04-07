using Tubester.Abstractions.Credits;

namespace Tubester.Application.Account;

public sealed record AccountSettingsDto(
    string UserId,
    string PreferredTheme,
    string PreferredLanguage,
    DateTimeOffset UpdatedAtUtc,
    SubscriptionSummaryDto? Subscription);
