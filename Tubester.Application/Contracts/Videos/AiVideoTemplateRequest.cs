using Tubester.Domain;

namespace Tubester.Application.Contracts.Videos;

public sealed class AiVideoTemplateRequest
{
    public required string TargetVideoId { get; init; }
    public required string PromptEnrichment { get; init; }
    public bool GenerateTitle { get; init; } = true;
    public bool GenerateDescription { get; init; } = true;
    public bool GenerateTags { get; init; } = true;
    public bool SuggestPlaylists { get; init; }
    public int ExpectedCreditCost { get; init; }
    public AiVideoOperationFlags GetRequestedAiOperations()
    {
        var operations = AiVideoOperationFlags.None;

        if (GenerateTitle)
        {
            operations |= AiVideoOperationFlags.Title;
        }

        if (GenerateDescription)
        {
            operations |= AiVideoOperationFlags.Description;
        }

        if (GenerateTags)
        {
            operations |= AiVideoOperationFlags.Tags;
        }

        if (SuggestPlaylists)
        {
            operations |= AiVideoOperationFlags.PlaylistSuggestion;
        }

        return operations;
    }
}
