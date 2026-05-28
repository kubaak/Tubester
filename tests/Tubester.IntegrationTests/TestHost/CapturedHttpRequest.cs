using System.Text.Json;
using Xunit;

namespace Tubester.IntegrationTests.TestHost;

public sealed record CapturedHttpRequest(
    HttpMethod Method,
    Uri? RequestUri,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Headers,
    IReadOnlyDictionary<string, IReadOnlyList<string>> ContentHeaders,
    string? Body)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public T DeserializeBody<T>()
    {
        Assert.False(string.IsNullOrWhiteSpace(Body));

        var result = JsonSerializer.Deserialize<T>(Body!, JsonOptions);

        Assert.NotNull(result);

        return result;
    }
}