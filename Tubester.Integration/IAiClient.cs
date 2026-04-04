namespace Tubester.Integration;

public interface IAiClient
{
    Task<(string Title, string Description, IEnumerable<string> tags)> SuggestMetadataAsync(string context,
        CancellationToken cancellationToken);

    Task<string?> SuggestReplyAsync(string videoTitle, IEnumerable<string> tags, string commentText,
        string language, CancellationToken cancellationToken);
}