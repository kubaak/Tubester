namespace Tubester.Domain;

public sealed class AccountSettings : Entity
{
    public string UserId { get; private set; } = null!;
    public string PreferredTheme { get; private set; } = null!;
    public string PreferredLanguage { get; private set; } = null!;
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static AccountSettings CreateDefault(string userId, DateTimeOffset nowUtc)
    {
        RequireUtc(nowUtc);

        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("User id is required.", nameof(userId));
        }

        return new AccountSettings
        {
            UserId = userId,
            PreferredTheme = "system",
            PreferredLanguage = "en",
            UpdatedAtUtc = nowUtc
        };
    }

    public void Apply(string preferredTheme, string preferredLanguage, DateTimeOffset nowUtc)
    {
        RequireUtc(nowUtc);

        if (string.IsNullOrWhiteSpace(preferredTheme))
        {
            throw new ArgumentException("Preferred theme is required.", nameof(preferredTheme));
        }

        if (string.IsNullOrWhiteSpace(preferredLanguage))
        {
            throw new ArgumentException("Preferred language is required.", nameof(preferredLanguage));
        }

        PreferredTheme = preferredTheme.Trim();
        PreferredLanguage = preferredLanguage.Trim();
        UpdatedAtUtc = nowUtc;
    }

    private AccountSettings()
    {
    }
}
