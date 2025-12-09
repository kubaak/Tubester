using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace YouTubester.IntegrationTests.TestHost;

public static class MockAuthenticationExtensions
{
    public const string TestScheme = "Test";
    public const string TestEmail = "test@example.com";
    public const string TestName = "Test User";
    public const string TestPicture = "https://example.com/test-picture.jpg";
    public const string TestSub = "test-user-id";

    public static IServiceCollection AddMockAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(options =>
            {
                options.DefaultScheme = TestScheme;
                options.DefaultAuthenticateScheme = TestScheme;
                options.DefaultChallengeScheme = TestScheme;
            })
            // Main test scheme (used by [Authorize] default policy)
            .AddScheme<AuthenticationSchemeOptions, MockAuthenticationHandler>(TestScheme, _ => { })
            // ForLogout/LoginWithGoogle to not crash:
            .AddScheme<AuthenticationSchemeOptions, MockAuthenticationHandler>(
                CookieAuthenticationDefaults.AuthenticationScheme, _ => { })
            .AddScheme<AuthenticationSchemeOptions, MockAuthenticationHandler>(
                GoogleDefaults.AuthenticationScheme, _ => { });

        var policy = new AuthorizationPolicyBuilder();
        services.AddAuthorizationBuilder()
            .SetDefaultPolicy(policy
                .RequireAuthenticatedUser()
                .AddAuthenticationSchemes(TestScheme)
                .Build())
            .AddPolicy("RequiresYouTubeWrite", writePolicy =>
            {
                writePolicy.RequireAuthenticatedUser();
                writePolicy.AddAuthenticationSchemes(TestScheme);
                writePolicy.RequireClaim("yt_write_granted", "true");
            });

        return services;
    }
}

public sealed class MockAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder),
        IAuthenticationSignOutHandler
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.Name, MockAuthenticationExtensions.TestName),
            new Claim(ClaimTypes.Email, MockAuthenticationExtensions.TestEmail),
            new Claim("picture", MockAuthenticationExtensions.TestPicture),
            new Claim(ClaimTypes.NameIdentifier, MockAuthenticationExtensions.TestSub),
            new Claim("email_verified", "true"), new Claim("yt_write_granted", "true")
        };

        var identity = new ClaimsIdentity(claims, MockAuthenticationExtensions.TestScheme);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, MockAuthenticationExtensions.TestScheme);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    public Task SignOutAsync(AuthenticationProperties? properties)
    {
        return Task.CompletedTask;
    }
}