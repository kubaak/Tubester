using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tubester.Abstractions.Account;
using Tubester.Abstractions.Credits;

namespace Tubester.Api;

/// <summary>
/// Credits Controller - manages user credit balances and admin grants
/// </summary>
[Route("api/credits")]
[Tags("Credits")]
[Authorize]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
public sealed class CreditsController(
    ICreditsStore creditsStore,
    ICurrentUserContext currentUserContext) : ApiControllerBase
{
    /// <summary>
    /// Gets the current credit balance for the authenticated user.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The current wallet balance and period information</returns>
    [HttpGet("balance")]
    [ProducesResponseType(typeof(CreditBalanceResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<CreditBalanceResponse>> GetBalance(CancellationToken cancellationToken)
    {
        var userId = currentUserContext.UserId;

        var wallet = await creditsStore.GetWalletAsync(userId!, cancellationToken);

        if (wallet is null)
        {
            return Ok(new CreditBalanceResponse
            {
                Balance = 0,
                PeriodStartUtc = null,
                PeriodEndUtc = null
            });
        }

        return Ok(new CreditBalanceResponse
        {
            Balance = wallet.Balance,
            PeriodStartUtc = wallet.PeriodStartUtc,
            PeriodEndUtc = wallet.PeriodEndUtc
        });
    }

    /// <summary>
    /// Gets the current costs for credit actions.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The current credit action costs</returns>
    [HttpGet("costs")]
    [ProducesResponseType(typeof(IReadOnlyList<CreditActionCostResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CreditActionCostResponse>>> GetCosts(CancellationToken cancellationToken)
    {
        var costs = await creditsStore.GetActionCostsAsync(cancellationToken);

        return Ok(costs.Select(cost => new CreditActionCostResponse
        {
            ActionType = cost.ActionType,
            Cost = cost.Cost
        }).ToList());
    }
    
    /// <summary>
    /// Response DTO for credit balance query
    /// </summary>
    public sealed class CreditBalanceResponse
    {
        public int Balance { get; init; }
        public DateTimeOffset? PeriodStartUtc { get; init; }
        public DateTimeOffset? PeriodEndUtc { get; init; }
    }
    
    /// <summary>
    /// Response DTO for credit action cost query
    /// </summary>
    public sealed class CreditActionCostResponse
    {
        public required string ActionType { get; init; }
        public required int Cost { get; init; }
    }
}