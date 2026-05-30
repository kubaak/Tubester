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

    private User()
    {
    }
}