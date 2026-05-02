using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Tubester.Abstractions.Users;
using Tubester.Application.Jobs;
using Tubester.Domain;
using Tubester.IntegrationTests.TestHost;
using Tubester.Persistence;
using Xunit;

namespace Tubester.IntegrationTests;

[Collection(nameof(TestCollection))]
public sealed class CommentScanTests(TestFixture fixture)
{
    private readonly TestHelpers _helpers = new(fixture);
    
    [Fact]
    public async Task ScanComments_CalledTwiceWithoutFirstCompleting_EnqueuesOnlyOneJob()
    {
        // Arrange
        await fixture.ResetDbAsync();
        await _helpers.SeedVideoTestDataAsync();
        
        // Act — first call acquires the lock
        var firstResponse = await fixture.HttpClient.GetAsync("/api/coments/pull");

        // Act — second call should be rejected because the lock is still held
        var secondResponse = await fixture.HttpClient.GetAsync("/api/coments/pull");

        // Assert
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);

        var enqueuedJobs = fixture.CapturingJobClient.GetEnqueued<CommentScanJob>();
        Assert.Single(enqueuedJobs);
    }
}
