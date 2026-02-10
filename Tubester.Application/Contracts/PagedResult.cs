namespace Tubester.Application.Contracts;

/// <summary>
/// Represents a paginated result set.
/// </summary>
/// <typeparam name="T">Type of items in the result.</typeparam>
public sealed record PagedResult<T>
{
    /// <summary>
    /// Items in the current page.
    /// </summary>
    public required IReadOnlyList<T> Items { get; init; }

    /// <summary>
    /// Token for fetching the next page, or null if this is the last page.
    /// </summary>
    public string? NextPageToken { get; init; }
}