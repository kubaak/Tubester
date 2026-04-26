namespace Tubester.Application.Contracts.Videos;

public sealed record AiVideoTemplateRequest(
    string TargetVideoId,
    string PromptEnrichment,
    bool GenerateTitle = true,
    bool GenerateDescription = true,
    bool GenerateTags = true,
    bool SuggestPlaylists = false
);
