using Tubester.Abstractions.ApplicationConfiguration;
using Tubester.Abstractions.Credits;
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
    public IReadOnlyCollection<ActionCost> ActionCosts { get; init; } =
    [
        new()
        {
            ActionType = nameof(CreditActionType.AiTitleEnqueued),
            Cost = TestConstants.AiTitleEnqueuedCost,
            IsEnabled = true,
            UpdatedAtUtc = TestFixture.TestingDateTimeOffset
        },
        new()
        {
            ActionType = nameof(CreditActionType.AiDescriptionEnqueued),
            Cost = TestConstants.AiDescriptionEnqueuedCost,
            IsEnabled = true,
            UpdatedAtUtc = TestFixture.TestingDateTimeOffset
        },
        new()
        {
            ActionType = nameof(CreditActionType.AiTagsEnqueued),
            Cost = TestConstants.AiTagsEnqueuedCost,
            IsEnabled = true,
            UpdatedAtUtc = TestFixture.TestingDateTimeOffset
        },
        new()
        {
            ActionType = nameof(CreditActionType.AiPlaylistSuggestionEnqueued),
            Cost = TestConstants.AiPlaylistSuggestionEnqueuedCost,
            IsEnabled = true,
            UpdatedAtUtc = TestFixture.TestingDateTimeOffset
        },
        new()
        {
            ActionType = nameof(CreditActionType.AiReplyGenerated),
            Cost = TestConstants.AiReplyGeneratedCost,
            IsEnabled = true,
            UpdatedAtUtc = TestFixture.TestingDateTimeOffset
        },
        new()
        {
            ActionType = nameof(CreditActionType.CopyTemplateExecuted),
            Cost = TestConstants.CopyTemplateExecutedCost,
            IsEnabled = true,
            UpdatedAtUtc = TestFixture.TestingDateTimeOffset
        },
        new()
        {
            ActionType = nameof(CreditActionType.AiTemplateSubmitted),
            Cost = TestConstants.AiTemplateSubmittedCost,
            IsEnabled = true,
            UpdatedAtUtc = TestFixture.TestingDateTimeOffset
        },
        new()
        {
            ActionType = nameof(CreditActionType.ReplyPostedToYouTube),
            Cost = TestConstants.ReplyPostedToYouTubeCost,
            IsEnabled = true,
            UpdatedAtUtc = TestFixture.TestingDateTimeOffset
        }
    ];
    public List<ApplicationConfiguration> ApplicationConfigurations { get; init; } =
    [
        ApplicationConfiguration.Create(ApplicationConfigurationKeys.AiProvider, AiProviders.Ollama, ConfigurationValueType.String, "", true, TestFixture.TestingDateTimeOffset)
    ];
    public bool CreateSubscription { get; init; } = true;
    public int MonthlyCredits { get; init; } = TestConstants.MonthlyCredits;
    public bool EnableCommentScan { get; init; } = true;
}