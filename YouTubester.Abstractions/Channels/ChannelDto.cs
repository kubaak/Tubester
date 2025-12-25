namespace YouTubester.Abstractions.Channels;

/// <summary>
/// Represents a YouTube channel that is known to the application.
/// </summary>
/// <param name="Id">Identifier of the channel.</param>
/// <param name="Name">Display name of the channel.</param>
/// <param name="UploadsPlaylistId">Identifier of the uploads playlist for the channel.</param>
/// <param name="ETag">Optional entity tag used for conditional requests.</param>
public sealed record ChannelDto(
    string Id,
    string Name,
    string UploadsPlaylistId,
    string? ETag
);
