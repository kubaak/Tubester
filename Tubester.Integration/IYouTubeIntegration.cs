using Tubester.Abstractions.Channels;
using Tubester.Integration.Dtos;

namespace Tubester.Integration;

public interface IYouTubeIntegration
{
    Task<ChannelDto?> GetChannelAsync(string channelId, CancellationToken cancellationToken);

    Task<UserChannelDto?> GetCurrentChannelAsync(string accessToken, CancellationToken cancellationToken);

    IAsyncEnumerable<VideoDto> GetAllVideosAsync(
        string uploadsPlaylistId,
        DateTimeOffset? publishedAfter,
        CancellationToken cancellationToken);

    Task ReplyAsync(string parentCommentId, string text, CancellationToken cancellationToken);

    Task UpdateVideoAsync(
        string videoId,
        string title,
        string description,
        IReadOnlyList<string> tags,
        string? categoryId,
        string? defaultLanguage,
        string? defaultAudioLanguage,
        CancellationToken cancellationToken);

    Task AddVideoToPlaylistAsync(string playlistId, string videoId, CancellationToken cancellationToken);

    IAsyncEnumerable<DetailedPlaylistDto> GetPlaylistsAsync(
        string channelId,
        CancellationToken cancellationToken);

    IAsyncEnumerable<string> GetPlaylistVideoIdsAsync(
        string playlistId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<VideoDto>> GetVideosAsync(
        IEnumerable<string> videoIds,
        CancellationToken cancellationToken);
}