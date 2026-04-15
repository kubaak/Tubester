namespace Tubester.Application.Contracts.Replies;

public sealed record DraftDecisionDto
{
    public required string CommentId { get; init; }
    public required string ApprovedText { get; init; }
}