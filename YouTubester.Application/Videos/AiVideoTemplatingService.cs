using YouTubester.Abstractions.Videos;
using YouTubester.Application.Contracts.Videos;
using YouTubester.Integration;

namespace YouTubester.Application.Videos;

public sealed class AiVideoTemplatingService(
    IAiClient aiClient,
    IVideoRepository videoRepository,
    IDateTimeOffsetProvider dateTimeOffsetProvider)
    : IAiVideoTemplatingService
{
    public async Task GenerateAiTemplateAsync(
        string channelId,
        AiVideoTemplateRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(channelId))
        {
            throw new ArgumentException("Channel id is required.", nameof(channelId));
        }

        var targetVideo = await videoRepository.GetVideoByIdAsync(channelId, request.TargetVideoId, cancellationToken)
                          ?? throw new ArgumentException($"Target video {request.TargetVideoId} not found in cache.");

        var (suggestedTitle, suggestedDescription, suggestedTags) =
            await aiClient.SuggestMetadataAsync(request.PromptEnrichment, cancellationToken);

        var newTitle = request.GenerateTitle ? suggestedTitle : targetVideo.Title ?? string.Empty;
        var newDescription =
            request.GenerateDescription ? suggestedDescription : targetVideo.Description ?? string.Empty;
        var newTags = request.GenerateTags ? SanitizeTags(suggestedTags.ToArray()) : targetVideo.Tags;

        var nowUtc = dateTimeOffsetProvider.GetUtcNowDateTimeOffset();
        targetVideo.ApplyDetails(
            newTitle,
            newDescription,
            targetVideo.PublishedAt,
            targetVideo.Duration,
            targetVideo.Visibility,
            newTags,
            targetVideo.CategoryId,
            targetVideo.DefaultLanguage,
            targetVideo.DefaultAudioLanguage,
            targetVideo.Location,
            targetVideo.LocationDescription,
            nowUtc,
            null,
            targetVideo.CommentsAllowed);

        await videoRepository.UpsertAsync(channelId, [targetVideo], cancellationToken);
    }

    private static string[] SanitizeTags(IReadOnlyList<string> tags)
    {
        var cleaned = tags
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var result = new List<string>();
        var total = 0;
        foreach (var tag in cleaned)
        {
            var add = tag.Length;
            if (total + add > 500)
            {
                break;
            }

            result.Add(tag);
            total += add;
        }

        return result.ToArray();
    }
}