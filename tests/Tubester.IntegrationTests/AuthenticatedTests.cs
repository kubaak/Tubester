using System.Net;
using System.Net.Http.Json;
using Xunit;
using Tubester.IntegrationTests.TestHost;

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
    public async Task Logout_WhenAuthenticated_ReturnsOk()
    {
        // Act
        var response = await fixture.HttpClient.PostAsync("api/auth/logout", null);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}