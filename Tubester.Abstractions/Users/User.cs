namespace Tubester.Abstractions.Users;

public sealed class User
{
    public string Id { get; private set; } = null!;
    public string? Email { get; private set; }
    public string? Name { get; private set; }
    public string? Picture { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? LastLoginAt { get; private set; }
    public bool IsNew { get; private set; }
    public bool IsDeleted { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public DateTimeOffset? ReactivatedAt { get; private set; }

    public static User Create(string id, string? email, string? name, string? picture, DateTimeOffset createdAt)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("User id must be a non-empty string.", nameof(id));
        }

        var user = new User
        {
            Id = id,
            Email = email,
            Name = name,
            Picture = picture,
            CreatedAt = createdAt,
            LastLoginAt = createdAt,
            IsNew = true
        };

        return user;
    }

    public void UpdateProfile(string? email, string? name, string? picture, DateTimeOffset loginAt)
    {
        Email = email;
        Name = name;
        Picture = picture;
        LastLoginAt = loginAt;
    }

    public void MarkAsExisting()
    {
        IsNew = false;
    }

    /// <summary>
    /// Marks the user account as deleted, clearing personal profile data while preserving the technical identity.
    /// </summary>
    /// <param name="deletedAt">The timestamp when the deletion occurred.</param>
    public void MarkAsDeleted(DateTimeOffset deletedAt)
    {
        Email = null;
        Name = null;
        Picture = null;
        IsDeleted = true;
        DeletedAt = deletedAt;
        ReactivatedAt = null;
    }

    /// <summary>
    /// Restores a deleted user account from a fresh Google login, clearing previous app data indicators.
    /// </summary>
    /// <param name="email">The new email from Google.</param>
    /// <param name="name">The new name from Google.</param>
    /// <param name="picture">The new picture from Google.</param>
    /// <param name="reactivatedAt">The timestamp when reactivation occurred.</param>
    public void RestoreFromFreshLogin(string? email, string? name, string? picture, DateTimeOffset reactivatedAt)
    {
        Email = email;
        Name = name;
        Picture = picture;
        LastLoginAt = reactivatedAt;
        IsDeleted = false;
        ReactivatedAt = reactivatedAt;
        IsNew = false;
    }

    private User()
    {
    }
}