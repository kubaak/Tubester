using System.Security.Claims;
using Hangfire.Dashboard;
using Microsoft.Extensions.Logging;

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
    /// Authorizes access to the Hangfire dashboard.
    /// </summary>
    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();
        var user = httpContext.User;

        Console.WriteLine("Hangfire Authorize invoked");
        Console.WriteLine($"Allowed emails: {string.Join(',',_allowedEmails)}");

        if (user.Identity is not { IsAuthenticated: true })
        {
            Console.WriteLine("Hangfire: user is not authenticated");
            return false;
        }

        var email =
            user.FindFirst(ClaimTypes.Email)?.Value ??
            user.FindFirst("email")?.Value;

        Console.WriteLine($"Hangfire: email = {email}");

        if (string.IsNullOrWhiteSpace(email))
        {
            Console.WriteLine("Hangfire: email claim missing");
            return false;
        }

        var allowed = _allowedEmails.Contains(email);
        Console.WriteLine($"Hangfire: allowed = {allowed}");

        return allowed;
    }
}