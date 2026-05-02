namespace Tubester.Abstractions.Channels;

/// <summary>
/// Represents a channel that belongs to a specific user account.
/// </summary>
/// <param name="Id">Identifier of the channel.</param>
/// <param name="Title">Display title of the channel.</param>
/// <param name="Picture">Optional uniform resource locator of the channel picture.</param>
/// <param name="UploadPlaylistId">The YouTube uploads playlist id for this channel.</param>
public sealed record UserChannelDto(
    string Id,
    string Title,
    string? Picture,
    string? UploadPlaylistId
);
