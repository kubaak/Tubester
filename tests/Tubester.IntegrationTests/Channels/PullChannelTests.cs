using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Tubester.Abstractions.Channels;
using Tubester.Abstractions.Users;
using Tubester.Domain;
using Tubester.IntegrationTests.TestHost;
using Tubester.Persistence;
using Xunit;

namespace Tubester.IntegrationTests.Channels;

[Collection(nameof(TestCollection))]
public class PullTests(TestFixture fixture)
{
    private readonly TestHelpers _helpers = new(fixture.ApiServices);

    [Fact]
    public async Task CreatesChannelWhenMissing()
    {
        // Arrange
        await fixture.CleanStateAsync();

        using (var setupScope = fixture.ApiServices.CreateScope())
        {
            var databaseContext = setupScope.ServiceProvider.GetRequiredService<TubesterDb>();

            databaseContext.Users.Add(User.Create(
                TestConstants.UserId,
                MockAuthenticationExtensions.TestEmail,
                MockAuthenticationExtensions.TestName,
                MockAuthenticationExtensions.TestPicture,
                TestFixture.TestingDateTimeOffset));

            await databaseContext.SaveChangesAsync();
        }

        var remoteChannel = new ChannelDto(
            TestConstants.ChannelId,
            "Pulled Channel",
            "PulledUploadsPlaylistId",
            "pulled-channel-etag");

        fixture.ApiFactory.MockYouTubeIntegration
            .Setup(x => x.GetChannelAsync(
                TestConstants.ChannelId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(remoteChannel);

        // Act
        var response = await fixture.HttpClient.PostAsync("/api/channels/pull", null);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var dto = await TestHelpers.DeserializeAsync<ChannelDto>(response);
        Assert.Equal(remoteChannel, dto);

        using var verificationScope = fixture.ApiServices.CreateScope();
        var verificationDatabaseContext = verificationScope.ServiceProvider.GetRequiredService<TubesterDb>();

        var channel = await verificationDatabaseContext.Channels
            .AsNoTracking()
            .SingleOrDefaultAsync(c => c.ChannelId == TestConstants.ChannelId);

        Assert.NotNull(channel);
        Assert.Equal(TestConstants.UserId, channel.UserId);
        Assert.Equal(remoteChannel.Name, channel.Name);
        Assert.Equal(remoteChannel.UploadsPlaylistId, channel.UploadsPlaylistId);
        Assert.Equal(remoteChannel.ETag, channel.ETag);
        Assert.Equal(TestFixture.TestingDateTimeOffset, channel.UpdatedAt);

        var channelSettings = await verificationDatabaseContext.ChannelSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(settings => settings.ChannelId == TestConstants.ChannelId);

        Assert.NotNull(channelSettings);
    }

    [Fact]
    public async Task WhenYouTubeChannelDoesNotExist_ReturnsNotFound()
    {
        // Arrange
        await fixture.CleanStateAsync();
        await _helpers.SeedTestDataAsync();

        fixture.ApiFactory.MockYouTubeIntegration
            .Setup(x => x.GetChannelAsync(
                TestConstants.ChannelId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChannelDto?)null);

        // Act
        var response = await fixture.HttpClient.PostAsync("/api/channels/pull", null);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
