namespace Tubester.Abstractions.Channels;

public interface ICurrentChannelContext
{
    /// <summary>
    /// Returns the YouTube channel id for the current session, or null if not available.
    /// </summary>
    string? ChannelId { get; }

    /// <summary>
    /// Returns the channel id for the current session, or throws if not available.
    /// Use this from code that requires a channel to be present.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when no channel id is available.</exception>
    string GetRequiredChannelId();
}