namespace Tubester.Abstractions.Exceptions;

public sealed class InsufficientBalanceException(string userId, int currentBalance, int requestedAmount)
    : Exception($"Insufficient balance for user '{userId}': current balance {currentBalance}, requested amount {requestedAmount}.")
{
    public string UserId => userId;
    public int CurrentBalance => currentBalance;
    public int RequestedAmount => requestedAmount;
}