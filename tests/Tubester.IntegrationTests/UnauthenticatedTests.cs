using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Tubester.IntegrationTests;

public sealed class UnauthenticatedTests(WebApplicationFactory<Api.Program> factory)
    : IClassFixture<WebApplicationFactory<Api.Program>>
{
    private readonly HttpClient _client =
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task Me_WhenNotAuthenticated_Returns401()
    {
        var response = await _client.GetAsync("api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location); // no redirect to /Account/Login
    }

    [Fact]
    public async Task Logout_WhenNotAuthenticated_Returns401()
    {
        // Act
        var response = await _client.GetAsync("api/auth/logout");

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/login?returnUrl=%2F", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task LoginWithGoogle_ReturnsRedirectChallenge()
    {
        var response = await _client.GetAsync("api/auth/login/google?returnUrl=/swagger/index.html");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
    }
}