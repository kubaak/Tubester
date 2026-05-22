namespace Tubester.Abstractions.Credits;

public enum SpendFailureReason
{
    None = 0,
    NoWallet = 1,
    WalletExpired = 2,
    InsufficientBalance = 3,
    Conflict = 4
}
