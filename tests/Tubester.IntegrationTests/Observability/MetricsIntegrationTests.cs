using System.Net;
using Tubester.IntegrationTests.TestHost;
using Xunit;

namespace Tubester.IntegrationTests.Observability;

[Collection(nameof(TestCollection))]
public class MetricsIntegrationTests(TestFixture fixture)
{
    [Fact]
    public async Task Metrics_ReturnsOk()
    {
        // Act
        var response = await fixture.HttpClient.GetAsync("/metrics");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Metrics_ReturnsPrometheusFormat()
    {
        // Act
        var response = await fixture.HttpClient.GetAsync("/metrics");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var content = await response.Content.ReadAsStringAsync();
        Assert.NotEmpty(content);
        
        // Prometheus metrics format typically starts with # HELP or # TYPE comments
        Assert.Contains("# HELP", content);
    }
}
