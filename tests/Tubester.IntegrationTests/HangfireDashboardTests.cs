using System.Net;
using Tubester.IntegrationTests.TestHost;
using Xunit;

namespace Tubester.IntegrationTests;

[Collection(nameof(TestCollection))]
public class HangfireDashboardTests(TestFixture fixture)
{
    [Fact]
    public async Task JobsEnqueuedOk()
    {
        var response = await fixture.HttpClient.GetAsync("/hangfire/jobs/enqueued");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
    
    [Fact]
    public async Task InvalidEndpointReturnsNotFound()
    {
        var response = await fixture.HttpClient.GetAsync("/hangfire/xxx");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}