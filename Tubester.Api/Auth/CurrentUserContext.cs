using System.Security.Claims;
using Tubester.Abstractions.Account;

namespace Tubester.Api.Auth;

public sealed class CurrentUserContext(IHttpContextAccessor httpContextAccessor) : ICurrentUserContext
{
    public string? UserId
    {
        get
        {
            var httpContext = httpContextAccessor.HttpContext;
            var user = httpContext?.User;
            if (user is null || !user.Identity?.IsAuthenticated == true)
            {
                return null;
            }

            return user.FindFirstValue(ClaimTypes.NameIdentifier);
        }
    }
}
