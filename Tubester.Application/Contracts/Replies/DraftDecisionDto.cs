namespace Tubester.Application.Contracts.Replies;

public sealed record DraftDecisionDto(
    string CommentId,
    string ApprovedText
);