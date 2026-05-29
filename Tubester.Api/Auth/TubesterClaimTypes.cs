namespace Tubester.Api.Auth;

/// <summary>
/// Custom claim types
/// </summary>
public static class TubesterClaimTypes
{
    /// <summary>
    /// YouTube permissions granted to the user
    /// </summary>
    public const string YouTubeReadGranted = "yt_read_granted";
    /// <summary>
    /// Indicates whether YouTube write permissions have been granted to the user
    /// </summary>
    public const string YouTubeWriteGranted = "yt_write_granted";
    /// <summary>
    /// Represents the unique identifier for a YouTube channel associated with the user.
    /// </summary>
    public const string YouTubeChannelId = "yt_channel_id";
    /// <summary>
    /// The title of the associated YouTube channel
    /// </summary>
    public const string YouTubeChannelTitle = "yt_channel_title";
    /// <summary>
    /// URL representing the profile picture of the user's YouTube channel
    /// </summary>
    public const string YouTubeChannelPicture = "yt_channel_picture";
    /// <summary>
    /// Identifier for the YouTube upload playlist associated with the user's channel
    /// </summary>
    public const string YouTubeUploadPlaylistId = "yt_upload_playlist_id";
}