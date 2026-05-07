using Tubester.Abstractions.ApplicationConfiguration;
using Tubester.Domain;
using Tubester.Persistence.Credits;

namespace Tubester.IntegrationTests.TestHost;

public sealed record TestDataOptions
{
    public string ChannelId { get; init; } = TestConstants.ChannelId;
    public string UploadsPlaylistId { get; init; } = TestConstants.UploadsPlaylistId;
    public string UserId { get; init; } = TestConstants.UserId;
    public string ChannelName { get; init; } = TestConstants.ChannelName;

    public IReadOnlyCollection<Video> Videos { get; init; } = [TestHelpers.GetTargetVideo()];
    public IReadOnlyCollection<Playlist> Playlists { get; init; } = [];
    public IReadOnlyCollection<VideoPlaylist> VideoPlaylists { get; init; } = [];
    public IReadOnlyCollection<Plan> Plans { get; init; } = [];
    public IReadOnlyCollection<Reply> Replies { get; init; } = [];
    public List<ApplicationConfiguration> ApplicationConfigurations { get; init; } = 
    [
        ApplicationConfiguration.Create(ApplicationConfigurationKeys.AiProvider, AiProviders.Ollama, ConfigurationValueType.String, "", true, TestFixture.TestingDateTimeOffset)
    ];
    public bool CreateSubscription { get; init; } = true;
    public int MonthlyCredits { get; init; } = TestConstants.MonthlyCredits;
    public bool EnableCommentScan { get; init; } = true;
}