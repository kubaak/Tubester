using System.Net;
using System.Net.Http.Json;
using Tubester.Application.Contracts.Videos;
using Tubester.Domain;
using Tubester.IntegrationTests.TestHost;
using Xunit;

namespace Tubester.IntegrationTests;

[Collection(nameof(TestCollection))]
public sealed class PlaylistsTests(TestFixture fixture)
{
    private readonly TestHelpers _helpers = new(fixture.ApiServices);

    [Fact]
    public async Task ListEndpoint_WhenPlaylistsExistForCurrentChannel_ReturnsPlaylists()
    {
        await fixture.CleanStateAsync();

        var playlist1 = Playlist.Create(
            "playlist-1",
            TestConstants.ChannelId,
            "Playlist One",
            "First playlist",
            PlaylistVisibility.Public,
            TestFixture.TestingDateTimeOffset);

        var playlist2 = Playlist.Create(
            "playlist-2",
            TestConstants.ChannelId,
            "Playlist Two",
            "Second playlist",
            PlaylistVisibility.Private,
            TestFixture.TestingDateTimeOffset);

        await _helpers.SeedTestDataAsync(new TestDataOptions
        {
            Playlists = [playlist1, playlist2]
        });

        var response = await fixture.HttpClient.GetAsync("/api/playlists");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var playlists = await response.Content.ReadFromJsonAsync<IReadOnlyList<PlaylistDto>>();
        Assert.NotNull(playlists);
        Assert.Equal(2, playlists.Count);

        Assert.Contains(playlists, playlist => playlist.Id == "playlist-1" && playlist.Name == "Playlist One");
        Assert.Contains(playlists, playlist => playlist.Id == "playlist-2" && playlist.Name == "Playlist Two");
    }

    [Fact]
    public async Task ListEndpoint_WhenNoPlaylistsExistForCurrentChannel_ReturnsEmptyList()
    {
        await fixture.CleanStateAsync();
        await _helpers.SeedTestDataAsync();

        var response = await fixture.HttpClient.GetAsync("/api/playlists");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var playlists = await response.Content.ReadFromJsonAsync<IReadOnlyList<PlaylistDto>>();
        Assert.NotNull(playlists);
        Assert.Empty(playlists);
    }
}
