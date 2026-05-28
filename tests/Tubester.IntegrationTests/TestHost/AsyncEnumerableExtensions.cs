namespace Tubester.IntegrationTests.TestHost;

/// <summary>
/// Extension methods for working with IAsyncEnumerable in tests.
/// </summary>
public static class AsyncEnumerableExtensions
{
    /// <summary>
    /// Converts an IAsyncEnumerable to a List for easier assertion in tests.
    /// </summary>
    public static async Task<List<T>> ToListAsync<T>(this IAsyncEnumerable<T> source)
    {
        var result = new List<T>();

        await foreach (var item in source)
        {
            result.Add(item);
        }

        return result;
    }
}