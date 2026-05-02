namespace Tubester.Application.Contracts.Replies;

/// <summary>
/// Search Suggested Replies Request
/// </summary>
public sealed class SearchSuggestedRepliesRequest
{
    public string? VideoId { get; init; }
    public string? OriginalComment { get; init; }
    public int? PageSize { get; init; }
    public string? PageToken { get; init; }
}