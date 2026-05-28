namespace Tubester.IntegrationTests.TestHost;

/// <summary>
/// Helper methods for generating fake YouTube API JSON responses in tests.
/// </summary>
public static class YouTubeJson
{
    /// <summary>
    /// Creates a playlistItemListResponse with the specified video IDs.
    /// </summary>
    public static string PlaylistItems(params string[] videoIds)
    {
        var items = string.Join(",", videoIds.Select(videoId =>
            "{\"kind\":\"youtube#playlistItem\",\"etag\":\"etag-" + videoId + "\",\"id\":\"item-" + videoId + "\",\"contentDetails\":{\"videoId\":\"" + videoId + "\"}}"));

        return "{\"kind\":\"youtube#playlistItemListResponse\",\"etag\":\"etag-playlist-items\",\"items\":[" + items + "]}";
    }

    /// <summary>
    /// Creates a playlistItemListResponse with a nextPageToken for pagination testing.
    /// </summary>
    public static string PlaylistItemsPage(string? nextPageToken, params string[] videoIds)
    {
        var items = string.Join(",", videoIds.Select(videoId =>
            "{\"kind\":\"youtube#playlistItem\",\"etag\":\"etag-" + videoId + "\",\"id\":\"item-" + videoId + "\",\"contentDetails\":{\"videoId\":\"" + videoId + "\"}}"));

        var nextPagePart = string.IsNullOrEmpty(nextPageToken) ? "" : ",\"nextPageToken\":\"" + nextPageToken + "\"";

        return "{\"kind\":\"youtube#playlistItemListResponse\",\"etag\":\"etag-page\"" + nextPagePart + ",\"items\":[" + items + "]}";
    }

    /// <summary>
    /// Creates a Google error response for playlist not found.
    /// </summary>
    public static string PlaylistNotFound()
    {
        return "{\"error\":{\"code\":404,\"message\":\"Playlist not found.\",\"errors\":[{\"message\":\"Playlist not found.\",\"domain\":\"youtube.playlistItem\",\"reason\":\"playlistNotFound\",\"location\":\"playlistId\",\"locationType\":\"parameter\"}]}}";
    }

    /// <summary>
    /// Creates an empty playlistItemListResponse.
    /// </summary>
    public static string EmptyPlaylistItems()
    {
        return "{\"kind\":\"youtube#playlistItemListResponse\",\"etag\":\"etag-empty\",\"items\":[]}";
    }

    /// <summary>
    /// Creates a videoListResponse with the specified video IDs.
    /// </summary>
    public static string VideosList(params string[] videoIds)
    {
        var items = string.Join(",", videoIds.Select(videoId =>
            "{\"kind\":\"youtube#video\",\"etag\":\"etag-" + videoId + "\",\"id\":\"" + videoId + "\",\"snippet\":{\"title\":\"Title " + videoId + "\",\"description\":\"Description " + videoId + "\",\"tags\":[\"tag1\",\"tag2\"],\"categoryId\":\"22\",\"publishedAt\":\"2026-01-01T00:00:00Z\"},\"contentDetails\":{\"duration\":\"PT1M10S\"},\"status\":{\"privacyStatus\":\"public\"}}"));

        return "{\"kind\":\"youtube#videoListResponse\",\"etag\":\"etag-videos\",\"items\":[" + items + "]}";
    }

    /// <summary>
    /// Creates a channelListResponse with a single channel.
    /// </summary>
    public static string Channel(string channelId, string title, string uploadsPlaylistId)
    {
        return "{\"kind\":\"youtube#channelListResponse\",\"etag\":\"etag-channel\",\"pageInfo\":{\"totalResults\":1,\"resultsPerPage\":1},\"items\":[{\"kind\":\"youtube#channel\",\"etag\":\"etag-" + channelId + "\",\"id\":\"" + channelId + "\",\"snippet\":{\"title\":\"" + title + "\",\"description\":\"Test channel description\",\"customUrl\":\"@testchannel\",\"publishedAt\":\"2020-01-01T00:00:00Z\"},\"contentDetails\":{\"relatedPlaylists\":{\"uploads\":\"" + uploadsPlaylistId + "\"}}}]}";
    }

    /// <summary>
    /// Creates a Google error response for comments disabled.
    /// </summary>
    public static string CommentsDisabled()
    {
        return "{\"error\":{\"code\":403,\"message\":\"Comments are disabled for this video.\",\"errors\":[{\"message\":\"Comments are disabled for this video.\",\"domain\":\"youtube.commentThread\",\"reason\":\"commentsDisabled\",\"locationType\":\"parameter\",\"location\":\"videoId\"}]}}";
    }

    /// <summary>
    /// Creates a playlistListResponse with the specified playlists.
    /// </summary>
    public static string Playlists(params (string Id, string Title, string Description, string Privacy)[] playlists)
    {
        var items = string.Join(",", playlists.Select(p =>
            "{\"kind\":\"youtube#playlist\",\"etag\":\"etag-" + p.Id + "\",\"id\":\"" + p.Id + "\",\"snippet\":{\"title\":\"" + p.Title + "\",\"description\":\"" + p.Description + "\",\"publishedAt\":\"2026-01-01T00:00:00Z\",\"tags\":[],\"defaultLanguage\":\"en\"},\"status\":{\"privacyStatus\":\"" + p.Privacy + "\"}}"));

        return "{\"kind\":\"youtube#playlistListResponse\",\"etag\":\"etag-playlists\",\"items\":[" + items + "]}";
    }
}