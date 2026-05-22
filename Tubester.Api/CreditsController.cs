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
    [ProducesResponseType(typeof(CreditActionCosts), StatusCodes.Status200OK)]
    public async Task<ActionResult<CreditActionCosts>> GetCosts(CancellationToken cancellationToken)
    {
        var costs = await creditsStore.GetActionCostsAsync(cancellationToken);

        var costsByActionType = costs.ToDictionary(
            c => c.ActionType,
            c => c.Cost,
            StringComparer.Ordinal);

        var response = new CreditActionCosts
        {
            CopyTemplateExecuted = GetRequiredCost(costsByActionType, CreditActionType.CopyTemplateExecuted),
            VideoDetailsSubmitted = GetRequiredCost(costsByActionType, CreditActionType.AiTemplateSubmitted),
            AiReplyGenerated = GetRequiredCost(costsByActionType, CreditActionType.AiReplyGenerated),
            ReplyPostedToYouTube = GetRequiredCost(costsByActionType, CreditActionType.ReplyPostedToYouTube),
            AiTitle = GetRequiredCost(costsByActionType, CreditActionType.AiTitleEnqueued),
            AiDescription = GetRequiredCost(costsByActionType, CreditActionType.AiDescriptionEnqueued),
            AiTags = GetRequiredCost(costsByActionType, CreditActionType.AiTagsEnqueued),
            AiPlaylist = GetRequiredCost(costsByActionType, CreditActionType.AiPlaylistSuggestionEnqueued),
        };

        return Ok(response);
    }

    private static int GetRequiredCost(
        IReadOnlyDictionary<string, int> costsByActionType,
        CreditActionType actionType)
    {
        var actionTypeName = actionType.ToString();

        if (!costsByActionType.TryGetValue(actionTypeName, out var cost))
        {
            throw new InvalidOperationException($"Missing credit action cost for action type '{actionTypeName}'.");
        }

        return cost;
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
    public sealed class CreditActionCosts
    {
        /// <summary>
        /// 
        /// </summary>
        public required int CopyTemplateExecuted { get; init; }
        /// <summary>
        /// 
        /// </summary>
        public required int VideoDetailsSubmitted { get; init; }
        /// <summary>
        /// 
        /// </summary>
        public required int AiReplyGenerated { get; init; }
        public required int ReplyPostedToYouTube { get; init; }
        public required int AiTitle { get; init; }
        public required int AiDescription { get; init; }
        public required int AiTags { get; init; }
        public required int AiPlaylist { get; init; }
    }
}