namespace Tubester.Application.Contracts.Replies;

public sealed record BatchDecisionRequest(
    DraftDecisionDto[] Decisions
);
