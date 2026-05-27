using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Tubester.IntegrationTests.TestHost;
using Xunit;

namespace Tubester.IntegrationTests;

[Collection(nameof(TestCollection))]
public sealed class AuthenticatedTests(TestFixture fixture)
{
    private sealed class MeResponse
    {
        public string? Name { get; set; }
        public string? Email { get; set; }
        public string? Sub { get; set; }
        public string? Picture { get; set; }
    }

    [Fact]
    public async Task Me_WhenAuthenticated_ReturnsMockUserInfo()
    {
        // Act
        var response = await fixture.HttpClient.GetAsync("api/auth/me");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<MeResponse>();
        Assert.NotNull(body);

        Assert.Equal(MockAuthenticationExtensions.TestName, body.Name);
        Assert.Equal(MockAuthenticationExtensions.TestEmail, body.Email);
        Assert.Equal(MockAuthenticationExtensions.TestSub, body.Sub);
        Assert.Equal(MockAuthenticationExtensions.TestPicture, body.Picture);
    }

    [Fact]
    public async Task Logout_WhenAuthenticated_RedirectsToLandingPage()
    {
        // Arrange
        var client = fixture.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        // Act
        var response = await client.GetAsync("api/auth/logout");

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/login?returnUrl=%2F", response.Headers.Location?.ToString());
    }
    
    [Fact]
    public async Task Logout_WithReturnUrl_RedirectsToLoginWithReturnUrl()
    {
        // Arrange
        var client = fixture.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        // Act
        var response = await client.GetAsync("api/auth/logout?returnUrl=%2Fdashboard");

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/login?returnUrl=%2Fdashboard", response.Headers.Location?.ToString());
    }
    
    [Fact]
    public async Task Logout_WithExternalReturnUrl_RedirectsToLandingPage()
    {
        // Arrange
        var client = fixture.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        // Act
        var response = await client.GetAsync("api/auth/logout?returnUrl=https%3A%2F%2Fevil.com");

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/login?returnUrl=%2F", response.Headers.Location?.ToString());
    }
}