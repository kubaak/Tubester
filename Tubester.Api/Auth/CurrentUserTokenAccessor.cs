using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Tubester.Abstractions.Auth;

namespace Tubester.Api.Auth;

public sealed class CurrentUserTokenAccessor(
    IHttpContextAccessor httpContextAccessor,
    IAuthenticationService authenticationService) : ICurrentUserTokenAccessor
{
    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext is null)
        {
            return null;
        }

        // Use the current authentication scheme (cookie) to retrieve the access token
        var authenticateResult = await authenticationService.AuthenticateAsync(
            httpContext,
            CookieAuthenticationDefaults.AuthenticationScheme);

        if (!authenticateResult.Succeeded)
        {
            return null;
        }

        var accessToken = authenticateResult.Properties?.GetTokenValue("access_token");
        return accessToken;
    }
}