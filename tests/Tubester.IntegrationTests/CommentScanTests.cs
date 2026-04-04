using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Moq;
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
    [Fact]
    public async Task ScanComments_CalledTwiceWithoutFirstCompleting_EnqueuesOnlyOneJob()
    {
        // Arrange
        await fixture.ResetDbAsync();

        const string testChannelId = "UCCommentScanLockTest";
        const string testChannelName = "CommentScanLockChannel";
        const string testUploadsPlaylistId = "PLCommentScanLockTest";
        const string userId = MockAuthenticationExtensions.TestSub;

        var dummyUser = User.Create(
            userId,
            MockAuthenticationExtensions.TestEmail,
            MockAuthenticationExtensions.TestName,
            MockAuthenticationExtensions.TestPicture,
            TestFixture.TestingDateTimeOffset);

        var dummyChannel = Channel.Create(
            testChannelId,
            userId,
            testChannelName,
            testUploadsPlaylistId,
            TestFixture.TestingDateTimeOffset);

        using (var scope = fixture.ApiServices.CreateScope())
        {
            var databaseContext = scope.ServiceProvider.GetRequiredService<TubesterDb>();
            databaseContext.Users.Add(dummyUser);
            databaseContext.Channels.Add(dummyChannel);
            await databaseContext.SaveChangesAsync();
        }

        fixture.ApiFactory.MockCurrentChannelContext
            .Setup(x => x.GetRequiredChannelId())
            .Returns(testChannelId);

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
