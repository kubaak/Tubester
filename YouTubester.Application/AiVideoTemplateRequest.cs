namespace YouTubester.Application;

public sealed record AiVideoTemplateRequest(
    string TargetVideoId,
    string PromptEnrichment,
    bool GenerateTitle = true,
    bool GenerateDescription = true,
    bool GenerateTags = true
);
