namespace Tubester.Application.Exceptions;

public class AiTemplatingNotStartedException : Exception
{
    public AiTemplatingNotStartedException() : base(
        "Target video not found for current channel or an AI template job is already in progress.")
    {
    }

    public AiTemplatingNotStartedException(string message) : base(message)
    {
    }

    public AiTemplatingNotStartedException(string message, Exception innerException) : base(message, innerException)
    {
    }
}