namespace Tubester.Application.Exceptions;

/// <summary>
/// Exception thrown when a page token is invalid or malformed.
/// </summary>
public sealed class InvalidPageTokenException : Exception
{
    public InvalidPageTokenException() : base("Page token is invalid or malformed.")
    {
    }

    public InvalidPageTokenException(string message) : base(message)
    {
    }

    public InvalidPageTokenException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Exception thrown when page size is outside valid range.
/// </summary>
public sealed class InvalidPageSizeException : Exception
{
    public InvalidPageSizeException(int pageSize, int maxPageSize)
        : base($"Page size must be between 1 and {maxPageSize}. Provided: {pageSize}")
    {
    }

    public InvalidPageSizeException(string message) : base(message)
    {
    }

    public InvalidPageSizeException(string message, Exception innerException) : base(message, innerException)
    {
    }
}