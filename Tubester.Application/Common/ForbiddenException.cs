namespace Tubester.Application.Common;

public class ForbiddenException : Exception
{
    public ForbiddenException() { }
    public ForbiddenException(string message) : base(message) { }
    public ForbiddenException(string message, Exception inner) : base(message, inner) { }
}