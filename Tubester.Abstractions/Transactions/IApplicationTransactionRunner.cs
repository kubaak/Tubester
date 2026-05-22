namespace Tubester.Abstractions.Transactions;

public interface IApplicationTransactionRunner
{
    Task ExecuteAsync(
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken);

    Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken);
}
