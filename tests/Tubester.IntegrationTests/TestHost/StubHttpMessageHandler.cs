using System.IO.Compression;
using System.Net;
using System.Text;

namespace Tubester.IntegrationTests.TestHost;

public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();

    public List<CapturedHttpRequest> Requests { get; } = [];

    public void EnqueueJson(HttpStatusCode statusCode, string json)
    {
        _responses.Enqueue(_ => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });
    }

    public void Enqueue(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
    {
        _responses.Enqueue(responseFactory);
    }

    public void Reset()
    {
        _responses.Clear();
        Requests.Clear();
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? null
            : await ReadRequestBodyAsync(request.Content, cancellationToken);

        Requests.Add(new CapturedHttpRequest(
            request.Method,
            request.RequestUri,
            request.Headers.ToDictionary(
                h => h.Key,
                h => (IReadOnlyList<string>)h.Value.ToList(),
                StringComparer.OrdinalIgnoreCase),
            request.Content?.Headers.ToDictionary(
                h => h.Key,
                h => (IReadOnlyList<string>)h.Value.ToList(),
                StringComparer.OrdinalIgnoreCase) ?? new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase),
            body));

        if (_responses.Count == 0)
        {
            throw new InvalidOperationException(
                $"No fake YouTube HTTP response configured for {request.Method} {request.RequestUri}. Body: {body}");
        }

        return _responses.Dequeue()(request);
    }

    private static async Task<string> ReadRequestBodyAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        var bytes = await content.ReadAsByteArrayAsync(cancellationToken);

        if (IsGzipEncoded(content, bytes))
        {
            await using var compressedStream = new MemoryStream(bytes);
            await using var gzipStream = new GZipStream(compressedStream, CompressionMode.Decompress);
            using var reader = new StreamReader(gzipStream, Encoding.UTF8);

            return await reader.ReadToEndAsync(cancellationToken);
        }

        return Encoding.UTF8.GetString(bytes);
    }

    private static bool IsGzipEncoded(HttpContent content, byte[] bytes)
    {
        var hasGzipHeader = content.Headers.ContentEncoding.Any(
            encoding => string.Equals(encoding, "gzip", StringComparison.OrdinalIgnoreCase));

        var hasGzipMagicBytes = bytes.Length >= 2 &&
                                bytes[0] == 0x1F &&
                                bytes[1] == 0x8B;

        return hasGzipHeader || hasGzipMagicBytes;
    }
}