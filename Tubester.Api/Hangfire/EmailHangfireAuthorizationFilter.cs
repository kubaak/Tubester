using System.Security.Claims;
using Hangfire.Dashboard;

namespace Tubester.Api.Hangfire;

/// <summary>
/// Restricts Hangfire dashboard access to authenticated users whose email is explicitly allowed.
/// </summary>
public sealed class EmailHangfireAuthorizationFilter : IDashboardAuthorizationFilter
{
    private readonly HashSet<string> _allowedEmails;

    /// <summary>
    /// Creates the authorization filter.
    /// </summary>
    public EmailHangfireAuthorizationFilter(IEnumerable<string> allowedEmails)
    {
        _allowedEmails = new HashSet<string>(
            allowedEmails
                .Where(e => !string.IsNullOrWhiteSpace(e))
                .Select(e => e.Trim()),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="context"></param>
    /// <returns></returns>
    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();
        var user = httpContext.User;

        if (user.Identity is not { IsAuthenticated: true })
        {
            return false;
        }

        var email =
            user.FindFirst(ClaimTypes.Email)?.Value ??
            user.FindFirst("email")?.Value;

        if (string.IsNullOrWhiteSpace(email))
        {
            return false;
        }

        return _allowedEmails.Contains(email);
    }
}