using System.Net;
using System.Net.Http.Json;
using Xunit;
using YouTubester.IntegrationTests.TestHost;

namespace YouTubester.IntegrationTests;

[Collection(nameof(TestCollection))]
public sealed class AuthenticatedTests(TestFixture fixture)
{
    private sealed class MeResponse
    {
        public string? name { get; set; }
        public string? email { get; set; }
        public string? sub { get; set; }
        public string? picture { get; set; }
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

        Assert.Equal(MockAuthenticationExtensions.TestName, body.name);
        Assert.Equal(MockAuthenticationExtensions.TestEmail, body.email);
        Assert.Equal(MockAuthenticationExtensions.TestSub, body.sub);
        Assert.Equal(MockAuthenticationExtensions.TestPicture, body.picture);
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