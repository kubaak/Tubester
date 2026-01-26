namespace YouTubester.Abstractions.Credits;

/// <summary>
/// Provides read-only access to credits and wallet data.
/// </summary>
public interface ICreditsReadModel
{
    /// <summary>
    /// Gets the wallet balance for the specified user.
    /// </summary>
    /// <param name="userId">Identifier of the user.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The current wallet balance.</returns>
    Task<int> GetWalletBalanceAsync(string userId, CancellationToken cancellationToken);
}
