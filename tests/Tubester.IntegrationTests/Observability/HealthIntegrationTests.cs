using System.Net;
using Tubester.IntegrationTests.TestHost;
using Xunit;

namespace Tubester.IntegrationTests.Observability;

[Collection(nameof(TestCollection))]
public class HealthIntegrationTests(TestFixture fixture)
{
    [Fact]
    public async Task HealthLive_ReturnsOk()
    {
        // Act
        var response = await fixture.HttpClient.GetAsync("/health/live");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task HealthReady_ReturnsOk()
    {
        // Act
        var response = await fixture.HttpClient.GetAsync("/health/ready");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
