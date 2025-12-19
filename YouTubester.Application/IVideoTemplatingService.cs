namespace YouTubester.Application;

public interface IVideoTemplatingService
{
    Task<CopyVideoTemplateResult> CopyTemplateAsync(
        string userId,
        CopyVideoTemplateRequest request,
        CancellationToken cancellationToken);

    Task GenerateAiTemplateAsync(
        string userId,
        AiVideoTemplateRequest request,
        CancellationToken cancellationToken);
}
