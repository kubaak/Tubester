namespace Tubester.Integration.Exceptions;

public sealed class CommentsDisabledException(string videoId, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public string VideoId { get; } = videoId;
}