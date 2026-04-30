using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tubester.Abstractions.Account;
using Tubester.Application.Common;
using Tubester.Application.Credits;

namespace Tubester.Api;

/// <summary>
/// Admin endpoints for managing user credits.
/// </summary>
[Route("api/admin/credits")]
[Tags("Admin Credits")]
[Authorize(Policy = "AdminEmail")]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public sealed class AdminCreditsController(
    ICreditsService creditsService,
    ICurrentUserContext currentUserContext) : ApiControllerBase
{
    /// <summary>
    /// Grants credits to a user.
    /// </summary>
    /// <param name="operationId">Client-generated idempotency operation id.</param>
    /// <param name="request">The grant request containing target user and amount.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result of the grant operation.</returns>
    [HttpPost("grants")]
    [ProducesResponseType(typeof(CreditGrantResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(CreditGrantResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(CreditGrantResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CreditGrantResponse>> Grant(
        [FromHeader(Name = "OperationId")] string? operationId,
        [FromBody] CreditGrantRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(operationId))
        {
            return BadRequest(new CreditGrantResponse
            {
                Success = false,
                Message = "Missing OperationId header."
            });
        }

        operationId = operationId.Trim();

        if (operationId.Length > 100)
        {
            return BadRequest(new CreditGrantResponse
            {
                Success = false,
                Message = "OperationId is too long."
            });
        }

        if (string.IsNullOrWhiteSpace(request.UserId))
        {
            return BadRequest(new CreditGrantResponse
            {
                Success = false,
                Message = "UserId is required."
            });
        }

        if (request.Amount <= 0)
        {
            return BadRequest(new CreditGrantResponse
            {
                Success = false,
                Message = "Amount must be greater than zero."
            });
        }
        
        var adminUserId = currentUserContext.UserId;

        if (string.IsNullOrWhiteSpace(adminUserId))
        {
            return BadRequest(new CreditGrantResponse
            {
                Success = false,
                Message = "Current admin user id is missing."
            });
        }

        try
        {
            var grantResult = await creditsService.GrantAdminCreditsAsync(
                adminUserId,
                request.UserId,
                request.Amount,
                operationId,
                cancellationToken);
            
            return Ok(new CreditGrantResponse
            {
                Success = grantResult.Granted,
                AlreadyProcessed = grantResult.AlreadyProcessed,
                NewBalance = grantResult.NewBalance,
                Message = grantResult.AlreadyProcessed
                    ? "Grant was already processed."
                    : grantResult.Granted
                        ? $"Granted {request.Amount} credits to user."
                        : grantResult.Reason ?? "Grant operation failed."
            });
        }
        catch (NotFoundException e)
        {
            return NotFound(new CreditGrantResponse
            {
                Success = false,
                Message = e.Message
            });
        }
        catch (BadRequestException  e)
        {
            return BadRequest(new CreditGrantResponse
            {
                Success = false,
                Message = e.Message
            });
        }       
    }

    public sealed class CreditGrantRequest
    {
        public string UserId { get; init; } = string.Empty;
        public int Amount { get; init; }
    }

    public sealed class CreditGrantResponse
    {
        public bool Success { get; init; }
        public bool AlreadyProcessed { get; init; }
        public int NewBalance { get; init; }
        public string Message { get; init; } = string.Empty;
    }
}