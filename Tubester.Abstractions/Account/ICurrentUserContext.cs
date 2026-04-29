namespace Tubester.Abstractions.Account;

public interface ICurrentUserContext
{
    string? UserId { get; }
    string? Email { get; }
}
