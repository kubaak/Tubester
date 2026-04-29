using Microsoft.AspNetCore.Authorization;
using Tubester.Abstractions.Account;

namespace Tubester.Api.Auth;

/// <summary>
/// Authorization requirement that checks if the user's email is in the allowed admin emails list.
/// </summary>
public class AdminEmailRequirement : IAuthorizationRequirement
{
    public IReadOnlyList<string> AllowedEmails { get; }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="allowedEmails"></param>
    public AdminEmailRequirement(IEnumerable<string> allowedEmails)
    {
        AllowedEmails = allowedEmails.Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(e => e.Trim())
            .ToList()
            .AsReadOnly();
    }
}

/// <summary>
/// Authorization handler that verifies the user's email is in the allowed admin emails list.
/// </summary>
public sealed class AdminEmailAuthorizationHandler(ICurrentUserContext currentUserContext)
    : AuthorizationHandler<AdminEmailRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        AdminEmailRequirement requirement)
    {
        var email = currentUserContext.Email;

        if (string.IsNullOrWhiteSpace(email))
        {
            return Task.CompletedTask;
        }

        if (requirement.AllowedEmails.Contains(email, StringComparer.OrdinalIgnoreCase))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}