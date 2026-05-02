using Hangfire;
using Tubester.Application.Videos;

namespace Tubester.Application.Jobs;

public sealed class AiTemplateJob(IAiVideoImprovingService aiVideoImprovingService)
{
    [Queue("ai-templating")]
    [AutomaticRetry(Attempts = 3, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    public async Task Run(
        AiVideoDetailsRequest request,
        IJobCancellationToken jobCancellationToken)
    {
        jobCancellationToken.ThrowIfCancellationRequested();

        await aiVideoImprovingService.GenerateAiTemplateAsync(request, jobCancellationToken.ShutdownToken);
    }
}

public sealed record AiVideoDetailsRequest(
    string ChannelId,
    string UploadPlaylistId,
    string TargetVideoId,
    string PromptEnrichment,
    bool GenerateTitle = true,
    bool GenerateDescription = true,
    bool GenerateTags = true,
    bool SuggestPlaylists = false
);