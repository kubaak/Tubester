namespace Tubester.Application.Contracts.Replies;

public sealed record BatchDecisionResultDto(
    int Total,
    int Succeeded,
    int Failed,
    IReadOnlyList<DraftDecisionResultDto> Items
);